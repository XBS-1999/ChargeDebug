using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

// ════════════════════════════════════════════════════════════════════════
//  UniFlash 烧写驱动（默认 TMS320F28377D + XDS100V3，芯片型号与分区可配置）
//  =========================================================================
//  原理：调用 TI UniFlash 的 CLI（dslite）烧写 COFF(.out) 映像。
//    · 路径自动检测：无需配置任何路径。把 UniFlash CLI 包（dslite.bat +
//      user_files 目录）放到软件目录（exe 同目录或其 UniFlashCli/uniflash
//      子目录，深度≤3），OpenAsync 自动定位 dslite、CLI 包目录，
//      并自动发现 user_files\\configs\*.ccxml 与 user_files\\settings\*.ufsettings。
//    · 芯片型号与分区可配置（uniflash_config.json 的 ChipModel 与各 Hex 字段，
//      可选；缺省回退 TMS320F28377D 内置表：boot.out→A/B 区（Sector 0-1，
//      0x080000-0x083FFF），app.out→其余区（Sector 2-31，0x084000-0x0BFFFF））。
//    · 实际烧写地址由 .out 内 linker cmd 的段定义决定，UniFlash 按段地址烧写，
//      分区仅用于日志/文档/擦除参考，不提供任何 UI 分区勾选。
//
//  dslite flash 命令（UniFlash v8.x）：
//    dslite.exe flash -c <ccxml> -l <settings.ufsettings> -e -f -v <boot.out> <app.out> [-r 0]
//    -c 目标配置(ccxml)   -l 加载 GUI 生成的烧写设置   -e 控制台进度
//    -f 烧写   -v 烧后校验   -r 0 烧后 CPU Reset（28377D）
//    退出码 0 = 成功，非 0 = 失败。
// ════════════════════════════════════════════════════════════════════════

namespace ChargeDebug.Service;

/// <summary>UniFlash 烧写配置（JSON 可序列化，分区写死，无 UI 勾选）</summary>
public sealed class UniFlashFlasherConfig
{
    /// <summary>dslite 可执行文件完整路径（dslite.bat / dslite.exe）；留空 = 自动在软件目录下定位</summary>
    public string DslitePath { get; set; } = "";

    /// <summary>CLI 包工作目录（含 user_files\\configs、user_files\\settings、user_files\images）；留空 = 自动取 dslite 所在目录</summary>
    public string CliPackagePath { get; set; } = "";

    /// <summary>ccxml 相对包路径；留空 = 自动在 user_files\\configs 下查找 *.ccxml</summary>
    public string CcxmlRelativePath { get; set; } = "";

    /// <summary>烧写设置文件相对包路径（UniFlash GUI 导出，含擦除/校验策略）；留空 = 自动在 user_files\\settings 下查找 *.ufsettings</summary>
    public string SettingsRelativePath { get; set; } = "";

    /// <summary>boot 映像固定名（A/B 区，分区写死；FlashBoot=false 时跳过）</summary>
    public string BootImageName { get; set; } = "boot.out";

    /// <summary>应用映像固定名（其余区）</summary>
    public string AppImageName { get; set; } = "app.out";

    /// <summary>是否连 boot 一起烧（产线首烧 true；仅升级应用可 false）</summary>
    public bool FlashBoot { get; set; } = true;

    /// <summary>烧写完成后执行 CPU Reset（-r 0，28377D 复位操作索引 0）。
    /// 默认 true：烧完自动复位，板子直接运行新固件，无需断电上电。
    /// 若产线追求极致提速可改 false，但烧完需手动断电重上电才能跑新固件。</summary>
    public bool ResetAfterFlash { get; set; } = true;

    /// <summary>烧写后是否执行读回校验（-v）。默认 false（产线提速，约省 20-30s/核）；
    /// 如需烧写结果确认，在代码中改为 true 或在配置中覆盖。</summary>
    public bool VerifyAfterFlash { get; set; } = false;

    /// <summary>命令超时（秒），0 = 不限</summary>
    public int TimeoutSeconds { get; set; } = 300;

    /// <summary>是否输出 dslite 进度到控制台（-e）</summary>
    public bool Verbose { get; set; } = true;

    // ═══════ 芯片型号与分区（可配置；缺省 = TMS320F28377D 内置表）═══════
    /// <summary>目标芯片型号（仅用于日志/标识；默认 TMS320F28377D）</summary>
    public string ChipModel { get; set; } = "TMS320F28377D";

    /// <summary>Flash 起始地址（hex，如 0x080000）</summary>
    public string FlashBaseHex { get; set; } = "0x080000";

    /// <summary>Flash 结束地址（含）</summary>
    public string FlashEndHex { get; set; } = "0x0BFFFF";

    /// <summary>Boot 区起始地址（A 区 Sector0）</summary>
    public string BootBeginHex { get; set; } = "0x080000";

    /// <summary>Boot 区结束地址（B 区 Sector1 末）</summary>
    public string BootEndHex { get; set; } = "0x083FFF";

    /// <summary>App 区起始地址（Sector2）</summary>
    public string AppBeginHex { get; set; } = "0x084000";

    /// <summary>App 区结束地址（Sector31 末）</summary>
    public string AppEndHex { get; set; } = "0x0BFFFF";

    /// <summary>全片擦除起始地址（含 boot 与应用区）</summary>
    public string EraseBeginHex { get; set; } = "0x080000";

    /// <summary>全片擦除结束地址</summary>
    public string EraseEndHex { get; set; } = "0x0BFFFF";

    // ═══════ 双核映射（dslite -n 核索引；参考 UniFlash 8.2 生成脚本：CPU1 默认 core0、CPU2 用 -n 2）═══════
    /// <summary>CPU1 对应的 UniFlash 核索引（dslite -n；默认 0）</summary>
    public int Cpu1CoreIndex { get; set; } = 0;

    /// <summary>CPU2 对应的 UniFlash 核索引（默认 2，来自 UniFlash 生成脚本 dslite-C28xx_CPU2.bat 的 -n 2）</summary>
    public int Cpu2CoreIndex { get; set; } = 2;

    /// <summary>解析后的芯片分区（hex 解析失败/缺省时回退 F28377D 内置表）</summary>
    public (uint FlashBase, uint FlashEnd, uint BootBegin, uint BootEnd,
            uint AppBegin, uint AppEnd, uint EraseBegin, uint EraseEnd) GetPartition()
    {
        return (
            ParseHex(FlashBaseHex, F28377D_Partition.FlashBase),
            ParseHex(FlashEndHex, F28377D_Partition.FlashEnd),
            ParseHex(BootBeginHex, F28377D_Partition.SectorA_Begin),
            ParseHex(BootEndHex, F28377D_Partition.SectorB_End),
            ParseHex(AppBeginHex, F28377D_Partition.App_Begin),
            ParseHex(AppEndHex, F28377D_Partition.App_End),
            ParseHex(EraseBeginHex, F28377D_Partition.EraseRange.Begin),
            ParseHex(EraseEndHex, F28377D_Partition.EraseRange.End)
        );
    }

    /// <summary>解析 "0x080000" 形式的十六进制地址；空/非法回退默认值</summary>
    private static uint ParseHex(string? hex, uint fallback)
    {
        try
        {
            return string.IsNullOrWhiteSpace(hex) ? fallback
                : Convert.ToUInt32(hex.Trim().Replace("0x", "").Replace("0X", ""), 16);
        }
        catch { return fallback; }
    }

    public static UniFlashFlasherConfig Load(string jsonPath)
    {
        return JsonSerializer.Deserialize<UniFlashFlasherConfig>(File.ReadAllText(jsonPath))
               ?? new UniFlashFlasherConfig();
    }

    /// <summary>保存当前配置为 JSON（与 Load 对称；自动创建父目录，UTF-8 缩进格式）</summary>
    public void Save(string jsonPath)
    {
        string? dir = Path.GetDirectoryName(jsonPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(jsonPath,
            JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>返回默认配置（TMS320F28377D + XDS100V3，分区写死）</summary>
    public static UniFlashFlasherConfig CreateDefault() => new UniFlashFlasherConfig();

    /// <summary>深拷贝，供运行时安全修改副本（如临时仅烧 app 而不影响原始配置）</summary>
    public UniFlashFlasherConfig Clone() => new UniFlashFlasherConfig
    {
        DslitePath = DslitePath,
        CliPackagePath = CliPackagePath,
        CcxmlRelativePath = CcxmlRelativePath,
        SettingsRelativePath = SettingsRelativePath,
        BootImageName = BootImageName,
        AppImageName = AppImageName,
        FlashBoot = FlashBoot,
        ResetAfterFlash = ResetAfterFlash,
        VerifyAfterFlash = VerifyAfterFlash,
        TimeoutSeconds = TimeoutSeconds,
        Verbose = Verbose,
        ChipModel = ChipModel,
        FlashBaseHex = FlashBaseHex,
        FlashEndHex = FlashEndHex,
        BootBeginHex = BootBeginHex,
        BootEndHex = BootEndHex,
        AppBeginHex = AppBeginHex,
        AppEndHex = AppEndHex,
        EraseBeginHex = EraseBeginHex,
        EraseEndHex = EraseEndHex,
        Cpu1CoreIndex = Cpu1CoreIndex,
        Cpu2CoreIndex = Cpu2CoreIndex,
    };

    /// <summary>
    /// 校验配置完整性，返回问题列表（空 = 通过）。
    /// baseDir 用于解析相对路径（缺省取当前目录）；DslitePath/CliPackagePath 支持相对与绝对路径。
    /// </summary>
    public IReadOnlyList<string> Validate(string? baseDir = null)
    {
        var errors = new List<string>();
        string root = baseDir ?? Directory.GetCurrentDirectory();

        // 路径字段留空 = 自动定位（OpenAsync 时在软件目录下查找），此处不作为错误
        if (!string.IsNullOrWhiteSpace(DslitePath))
        {
            string dsliteFull = Path.IsPathRooted(DslitePath) ? DslitePath : Path.Combine(root, DslitePath);
            if (!File.Exists(dsliteFull))
                errors.Add($"dslite 不存在: {dsliteFull}");
        }

        if (!string.IsNullOrWhiteSpace(CliPackagePath))
        {
            string pkgFull = Path.IsPathRooted(CliPackagePath) ? CliPackagePath : Path.Combine(root, CliPackagePath);
            if (!Directory.Exists(pkgFull))
                errors.Add($"CLI 包目录不存在: {pkgFull}");
        }

        if (string.IsNullOrWhiteSpace(ChipModel))
            errors.Add("ChipModel 未设置（目标芯片型号）");
        if (string.IsNullOrWhiteSpace(CcxmlRelativePath))
            errors.Add("CcxmlRelativePath 未设置");
        if (string.IsNullOrWhiteSpace(SettingsRelativePath))
            errors.Add("SettingsRelativePath 未设置");
        if (string.IsNullOrWhiteSpace(BootImageName))
            errors.Add("BootImageName 未设置");
        if (string.IsNullOrWhiteSpace(AppImageName))
            errors.Add("AppImageName 未设置");
        if (TimeoutSeconds < 0)
            errors.Add("TimeoutSeconds 不能为负数");
        if (Cpu1CoreIndex < 0 || Cpu2CoreIndex < 0)
            errors.Add("Cpu1CoreIndex / Cpu2CoreIndex 不能为负数");

        return errors;
    }
}

/// <summary>
/// TMS320F28377D Flash 写死分区表（28377D 单核 512KB = 256KW，Bank0 Sector 0-31，每 sector 8KW=16KB）。
/// 仅供文档/审计/擦除范围使用；实际烧写地址由 .out 内 linker cmd 段定义决定。
/// </summary>
public static class F28377D_Partition
{
    public const string Device = "TMS320F28377D";

    /// <summary>Flash 起始地址</summary>
    public const uint FlashBase = 0x080000;

    /// <summary>Flash 结束地址（含，Bank0 Sector31 末）</summary>
    public const uint FlashEnd = 0x0BFFFF;

    /// <summary>A 区（Sector 0）</summary>
    public const uint SectorA_Begin = 0x080000;
    public const uint SectorA_End = 0x081FFF;

    /// <summary>B 区（Sector 1）</summary>
    public const uint SectorB_Begin = 0x082000;
    public const uint SectorB_End = 0x083FFF;

    /// <summary>应用区（Sector 2-31）</summary>
    public const uint App_Begin = 0x084000;
    public const uint App_End = 0x0BFFFF;

    /// <summary>全片擦除范围（含 boot 与应用区）</summary>
    public static (uint Begin, uint End) EraseRange => (FlashBase, FlashEnd);
}


/// <summary>烧写结果（ChargeDebug 单文件版内联，替代 PMATE.Core 的 FlashResult）</summary>
public sealed class FlashResult
{
    public bool Pass { get; }
    public string Version { get; }
    public string Checksum { get; }
    public string Message { get; }

    public FlashResult(bool pass, string version, string checksum, string message)
    {
        Pass = pass;
        Version = version;
        Checksum = checksum;
        Message = message;
    }
}

/// <summary>
/// UniFlash CLI 烧写器（ChargeDebug 单文件版）：封装 dslite 进程调用，无 PMATE 依赖。
/// 分区写死（boot→A/B 区，app→其余区），UI 无需任何勾选。
/// </summary>
public sealed class UniFlashFlasher
{
    private readonly UniFlashFlasherConfig _cfg;

    public UniFlashFlasher(UniFlashFlasherConfig config)
    {
        _cfg = config;
        Name = $"UniFlash-{(_cfg.ChipModel ?? F28377D_Partition.Device)}";
    }

    public string Name { get; }

    /// <summary>烧写过程/进度事件（行文本）</summary>
    public event Action<string>? Output;

    /// <summary>阶段进度：0~100</summary>
    public event Action<int, string>? Progress;

    public bool IsOpen { get; private set; }

    public Task OpenAsync(CancellationToken ct = default)
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;

        // 路径留空 → 在软件目录下自动定位 dslite 与 CLI 包
        if (string.IsNullOrWhiteSpace(_cfg.DslitePath) || string.IsNullOrWhiteSpace(_cfg.CliPackagePath))
        {
            var loc = Locate(baseDir);
            if (loc == null)
            {
                throw new FileNotFoundException(
                    $"未在软件目录 {baseDir}（含 UniFlashCli/uniflash 等子目录，深度≤3）或 C 盘固定目录\n" +
                    $"(C:\\UniFlashCLI\\uniflash_windows 等，见 FixedCliRoots) 下找到 dslite.bat/dslite.exe。\n" +
                    "请将 UniFlash CLI 包（dslite.bat + user_files 目录）放入软件目录后重试。");
            }
            _cfg.DslitePath = loc.Value.DslitePath;
            _cfg.CliPackagePath = loc.Value.CliPackagePath;
            Output?.Invoke($"已自动定位 dslite: {_cfg.DslitePath}");
            Output?.Invoke($"已自动定位 CLI 包: {_cfg.CliPackagePath}");
        }

        if (!File.Exists(_cfg.DslitePath))
        {
            throw new FileNotFoundException($"找不到 dslite: {_cfg.DslitePath}（请放入软件目录或显式配置 DslitePath）");
        }
        if (!Directory.Exists(_cfg.CliPackagePath))
        {
            throw new DirectoryNotFoundException($"找不到 CLI 包目录: {_cfg.CliPackagePath}");
        }

        // ccxml / settings 留空 → 自动发现
        if (string.IsNullOrWhiteSpace(_cfg.CcxmlRelativePath))
        {
            string? f = FindFirst(_cfg.CliPackagePath, "user_files", "configs", "*.ccxml");
            if (f == null)
                throw new FileNotFoundException($"未在 CLI 包目录 {_cfg.CliPackagePath} 下找到 user_files\\configs\\*.ccxml（请在 UniFlash GUI 生成目标芯片会话）");
            _cfg.CcxmlRelativePath = MakeRelative(_cfg.CliPackagePath, f);
            Output?.Invoke($"已自动发现 ccxml: {_cfg.CcxmlRelativePath}");
        }
        if (string.IsNullOrWhiteSpace(_cfg.SettingsRelativePath))
        {
            string? f = FindFirst(_cfg.CliPackagePath, "user_files", "settings", "*.ufsettings");
            if (f == null)
                throw new FileNotFoundException($"未在 CLI 包目录 {_cfg.CliPackagePath} 下找到 user_files\\settings\\*.ufsettings（请在 UniFlash GUI 生成烧写设置）");
            _cfg.SettingsRelativePath = MakeRelative(_cfg.CliPackagePath, f);
            Output?.Invoke($"已自动发现 settings: {_cfg.SettingsRelativePath}");
        }

        IsOpen = true;
        return Task.CompletedTask;
    }

    public Task CloseAsync()
    {
        IsOpen = false;
        return Task.CompletedTask;
    }

    public Task<string> IdentifyAsync(CancellationToken ct = default)
        => Task.FromResult($"TI UniFlash dslite @ {_cfg.DslitePath}");

    /// <summary>
    /// 烧写固件。firmwarePath = 应用映像 .out 绝对路径；
    /// boot 映像路径由配置 FlashBoot 时从固件目录约定名（boot.out）取，或另行传入（见 FlashAsync 重载）。
    /// </summary>
    public Task<FlashResult> FlashAsync(string firmwarePath, string serialNumber, CancellationToken ct = default)
        => FlashAsync(firmwarePath, serialNumber, 0, ct);

    /// <summary>烧写固件并指定目标 CPU 核（F28377D 双核：core 0=CPU1，core 1=CPU2）。</summary>
    public Task<FlashResult> FlashAsync(string firmwarePath, string serialNumber, int coreNumber, CancellationToken ct = default)
    {
        string? bootPath = _cfg.FlashBoot
            ? Path.Combine(Path.GetDirectoryName(firmwarePath)!, _cfg.BootImageName)
            : null;
        return FlashAsync(bootPath, firmwarePath, serialNumber, coreNumber, ct);
    }

    /// <summary>显式双映像烧写：boot 与 app 分别给出路径（boot/app 均可空，但至少提供一个）。</summary>
    public Task<FlashResult> FlashAsync(string? bootOutPath, string? appOutPath,
        string serialNumber, CancellationToken ct = default)
        => FlashAsync(bootOutPath, appOutPath, serialNumber, 0, ct);

    /// <summary>显式双映像烧写并指定目标 CPU 核（UniFlash 多核设备一次仅烧一核，-n 指定）。
    /// boot/app 均可空：只刷 Boot（app=null）或只刷 App（boot=null）都支持，但至少提供一个。</summary>
    public async Task<FlashResult> FlashAsync(string? bootOutPath, string? appOutPath,
        string serialNumber, int coreNumber, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        if (bootOutPath == null && appOutPath == null)
            throw new ArgumentException("boot/app 至少需提供一个 .out 文件");
        try
        {
            if (!IsOpen) await OpenAsync(ct);
            string? appDest = null;
            if (appOutPath != null)
            {
                // .out 用配置的目标名（默认 app.out，向后兼容）；.hex/.bin 等保留源文件名，
                // DSLite 按扩展名识别文件格式（官方支持 Intel Hex/S-Record/TI-TXT/binary）。
                string appImageName = Path.GetExtension(appOutPath).Equals(".out", StringComparison.OrdinalIgnoreCase)
                    ? _cfg.AppImageName
                    : Path.GetFileName(appOutPath);
                appDest = Path.Combine(_cfg.CliPackagePath, "user_files", "images", appImageName);
                CopyImage(appOutPath, appDest);
            }

            // 参数一律使用相对路径（正斜杠），与 UniFlash 官方生成脚本一致：
            // DSLite 12.2 对绝对路径/带引号的 ccxml 参数解析会 Failed to parse
            var args = new List<string> { "flash", "-c", ToCliPath(_cfg.CcxmlRelativePath) };
            if (!string.IsNullOrWhiteSpace(_cfg.SettingsRelativePath))
            {
                args.Add("-l");
                args.Add(ToCliPath(_cfg.SettingsRelativePath));
            }
            // 覆盖 settings 默认值（查询 -S .* 实测：FlashEraseSelection=Entire Flash、
            // VerifyAfterProgramLoad=Fast verification）：
            //  ① 擦除范围=必要扇区：只擦本次镜像实际占用 sector（只刷 App 时 Boot 区自动保留）
            //  ② 烧后自动验证=关：settings 默认 Fast verification 会额外耗时（约 15s/核），官方 bat 同款覆盖
            args.Add("-s");
            args.Add("FlashEraseSelection=Necessary Sectors Only (for Program Load)");
            args.Add("-s");
            args.Add("VerifyAfterProgramLoad=No verification");
            if (_cfg.Verbose) args.Add("-e");
            args.Add("-f");
            if (_cfg.VerifyAfterFlash) args.Add("-v");
            int uniflashCore = coreNumber <= 0 ? _cfg.Cpu1CoreIndex : _cfg.Cpu2CoreIndex;
            args.Add("-n");
            args.Add(uniflashCore.ToString());

            var part = _cfg.GetPartition();
            string? bootDest = null;
            if (_cfg.FlashBoot && bootOutPath != null)
            {
                string bootImageName = Path.GetExtension(bootOutPath).Equals(".out", StringComparison.OrdinalIgnoreCase)
                    ? _cfg.BootImageName
                    : Path.GetFileName(bootOutPath);
                bootDest = Path.Combine(_cfg.CliPackagePath, "user_files", "images", bootImageName);
                CopyImage(bootOutPath, bootDest);
                args.Add(ToCliPath(bootDest));
                Progress?.Invoke(15, $"boot 映像已就位（A/B 区 0x{part.BootBegin:X6}-0x{part.BootEnd:X6}）");
            }
            if (appDest != null)
            {
                args.Add(ToCliPath(appDest));
                Progress?.Invoke(20, $"app 映像已就位（应用区 0x{part.AppBegin:X6}-0x{part.AppEnd:X6}）");
            }

            if (_cfg.ResetAfterFlash)
            {
                args.Add("-r");
                args.Add("0");
            }

            Output?.Invoke($"[{DateTime.Now:HH:mm:ss}] SN={serialNumber} 开始烧写 {_cfg.ChipModel} CPU{coreNumber + 1}");
            Output?.Invoke("dslite " + string.Join(" ", args));

            int exit = await RunDsliteAsync(args, ct);
            string sha = Sha256File(appDest ?? bootDest!);
            bool pass = exit == 0;
            string message = pass
                ? (_cfg.VerifyAfterFlash ? $"烧写并校验成功（{sw.Elapsed.TotalSeconds:F1}s）"
                                         : $"烧写成功（未校验，{sw.Elapsed.TotalSeconds:F1}s）")
                : $"烧写失败，dslite 退出码 {exit}（见上方输出）";
            Output?.Invoke($"[{DateTime.Now:HH:mm:ss}] {message}");
            Progress?.Invoke(pass ? 100 : 0, message);
            return new FlashResult(pass, "", sha, message);
        }
        catch (OperationCanceledException)
        {
            Progress?.Invoke(0, "烧写已取消");
            return new FlashResult(false, "", "", "烧写已取消");
        }
        catch (Exception ex)
        {
            Output?.Invoke($"[{DateTime.Now:HH:mm:ss}] 异常: {ex.Message}");
            Progress?.Invoke(0, $"烧写异常: {ex.Message}");
            return new FlashResult(false, "", "", ex.Message);
        }
    }

    // ---------- 自动定位 ----------

    /// <summary>
    /// 在 baseDir（默认软件目录）下自动定位 dslite 与 CLI 包目录，无需任何配置。
    /// 定位顺序：
    ///   1) 包内 DSLite 可执行文件（ccs_base\DebugServer\bin\DSLite / DSLite.exe，
    ///      自包含，来自 Standalone Command Line 生成的 CLI 包）→ CLI 包目录 = 包根（ccs_base 同级）
    ///   2) 通用启动器 dslite.bat / dslite.exe（UniFlash 安装目录版）→ CLI 包目录 = 所在目录
    /// 候选目录：baseDir 根、UniFlashCli/uniflash/uniflash_cli/UniFlash 子目录，再递归（深度≤3）；
    /// 多个候选时优先同目录含 user_files 的。
    /// 返回 null = 未找到。
    /// </summary>
    /// <summary>C 盘固定 CLI 包候选根（纯 ASCII，规避中文路径问题；可自行增删）。</summary>
    internal static readonly string[] FixedCliRoots =
        { @"C:\UniFlashCLI", @"C:\UniFlash", @"C:\ChargeDebug" };

    public static (string DslitePath, string CliPackagePath)? Locate(string? baseDir = null)
    {
        if (string.IsNullOrWhiteSpace(baseDir))
            baseDir = AppDomain.CurrentDomain.BaseDirectory;

        var dirs = new List<string>();
        foreach (var sub in new[] { "", "UniFlashCli", "uniflash", "uniflash_cli", "UniFlash" })
        {
            string dir = sub.Length == 0 ? baseDir : Path.Combine(baseDir, sub);
            if (Directory.Exists(dir) && !dirs.Contains(dir)) dirs.Add(dir);
        }
        // C 盘固定候选根：用户可将 CLI 包（uniflash_windows 或包内容）放到以下任一位置；
        // 纯 ASCII 路径，规避中文路径兼容问题。根本身是包，或其 uniflash_windows 等子目录是包，均可命中。
        foreach (var root in FixedCliRoots)
        {
            if (Directory.Exists(root) && !dirs.Contains(root)) dirs.Add(root);
            foreach (var sub in new[] { "uniflash_windows", "UniFlashCli", "uniflash", "uniflash_cli", "UniFlash" })
            {
                string d = Path.Combine(root, sub);
                if (Directory.Exists(d) && !dirs.Contains(d)) dirs.Add(d);
            }
        }
        CollectDirs(baseDir, 3, dirs);

        // 候选1：包内 DSLite 可执行（自包含，优先）；CLI 包目录 = 包根
        foreach (var dir in dirs)
        {
            string? exe = FindDsliteExecutable(dir);
            if (exe != null)
            {
                if (Directory.Exists(Path.Combine(dir, "user_files")))
                    return (exe, dir);
            }
        }
        foreach (var dir in dirs)
        {
            string? exe = FindDsliteExecutable(dir);
            if (exe != null)
                return (exe, dir);
        }

        // 候选2：通用启动器 dslite.bat / dslite.exe，优先同目录含 user_files
        foreach (var dir in dirs)
        {
            foreach (var name in new[] { "dslite.bat", "dslite.exe" })
            {
                string f = Path.Combine(dir, name);
                if (File.Exists(f) && Directory.Exists(Path.Combine(dir, "user_files")))
                    return (f, dir);
            }
        }
        foreach (var dir in dirs)
        {
            foreach (var name in new[] { "dslite.bat", "dslite.exe" })
            {
                string f = Path.Combine(dir, name);
                if (File.Exists(f)) return (f, dir);
            }
        }
        return null;
    }

    /// <summary>在 dir 下找 ccs_base\DebugServer\bin\DSLite(.exe)，返回可执行文件路径（无则 null）</summary>
    private static string? FindDsliteExecutable(string dir)
    {
        string bin = Path.Combine(dir, "ccs_base", "DebugServer", "bin");
        if (!Directory.Exists(bin)) return null;
        foreach (var name in new[] { "DSLite", "DSLite.exe" })
        {
            string f = Path.Combine(bin, name);
            if (File.Exists(f)) return f;
        }
        return null;
    }

    /// <summary>收集 baseDir 下深度≤depth 的所有子目录（去重）</summary>
    private static void CollectDirs(string root, int depth, List<string> acc)
    {
        if (depth <= 0 || !Directory.Exists(root)) return;
        try
        {
            foreach (var d in Directory.GetDirectories(root))
            {
                if (!acc.Contains(d)) acc.Add(d);
                CollectDirs(d, depth - 1, acc);
            }
        }
        catch { /* 权限等异常跳过该目录 */ }
    }

    /// <summary>在 root\sub1\sub2 下按 pattern 找第一个文件（优先 generated.* / tms320f28377d*）</summary>
    private static string? FindFirst(string root, string sub1, string sub2, string pattern)
    {
        string dir = Path.Combine(root, sub1, sub2);
        if (!Directory.Exists(dir)) return null;
        string[] files;
        try { files = Directory.GetFiles(dir, pattern); }
        catch { return null; }
        if (files.Length == 0) return null;
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        foreach (var f in files)
        {
            string n = Path.GetFileName(f);
            if (n.StartsWith("generated", StringComparison.OrdinalIgnoreCase) ||
                n.StartsWith("tms320f28377d", StringComparison.OrdinalIgnoreCase))
                return f;
        }
        return files[0];
    }

    private static string MakeRelative(string root, string file)
        => Path.GetRelativePath(root, file).Replace('/', Path.DirectorySeparatorChar);

    // ---------- 内部 ----------

    private string Resolve(string relative)
        => Path.IsPathRooted(relative) ? relative : Path.Combine(_cfg.CliPackagePath, relative);

    /// <summary>转为相对 CLI 包根目录的路径并用正斜杠（DSLite 参数格式，对齐官方生成脚本）。
    /// 输入可为相对路径（user_files\...）或绝对路径（含包根前缀），统一归一化。</summary>
    private static string ToCliPath(string p)
    {
        if (string.IsNullOrWhiteSpace(p)) return p;
        int idx = p.IndexOf("user_files", StringComparison.OrdinalIgnoreCase);
        string rel = idx >= 0 ? p.Substring(idx) : p;
        return rel.Replace('\\', '/').TrimStart('/');
    }

    private static void CopyImage(string src, string dest)
    {
        if (!File.Exists(src))
            throw new FileNotFoundException($"找不到固件文件: {src}");
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(src, dest, overwrite: true);
    }

    /// <summary>
    /// 返回 DSLite 可写的 TI_APPDATA_DIR：优先 CLI 包内 ti_appdata（随包走、换机不变），
    /// 包目录不可写时兜底到系统临时目录。
    /// </summary>
    private string EnsureTiAppDataDir()
    {
        // TI 工具对非 ASCII 路径兼容性不稳：CLI 包目录含中文等字符时，
        // 不再把 TI_APPDATA_DIR 放进包内，改用纯 ASCII 的系统临时目录，
        // 彻底绕开中文路径（软件目录建议也改为纯英文）。
        var candidates = new List<string>();
        if (IsAsciiPath(_cfg.CliPackagePath))
            candidates.Add(Path.Combine(_cfg.CliPackagePath, "ti_appdata"));
        candidates.Add(Path.Combine(Path.GetTempPath(), "ChargeDebug_TI_AppData"));
        candidates.Add(Path.GetTempPath());
        foreach (var d in candidates)
        {
            try { Directory.CreateDirectory(d); return d; }
            catch { /* 尝试下一个候选 */ }
        }
        return Path.GetTempPath();
    }

    private static bool IsAsciiPath(string s) => s.All(c => c < 128);

    private async Task<int> RunDsliteAsync(List<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            WorkingDirectory = _cfg.CliPackagePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        // DSLite 默认把配置写到 %LOCALAPPDATA%\TEXAS INSTRUMENTS\CCS\...，
        // 换电脑/换用户时该路径可能不可写或含非法字符导致 create_directories 失败退出 -1。
        // 强制重定向到程序目录下可写子目录（TI 官方提示：set TI_APPDATA_DIR）。
        psi.EnvironmentVariables["TI_APPDATA_DIR"] = EnsureTiAppDataDir();

        string dslite = _cfg.DslitePath;
        if (dslite.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
        {
            // .bat 需经 cmd.exe 启动；参数整体传递，避免引号丢失
            psi.FileName = "cmd.exe";
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add("\"" + dslite + "\"");
        }
        else
        {
            psi.FileName = dslite;
        }
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi };
        var outBuf = new System.Text.StringBuilder();
        int lastPct = 0; // 进度只增不减，避免阶段百分比回跳
        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            lock (outBuf) outBuf.AppendLine(e.Data);
            Output?.Invoke(e.Data);
            // 实时进度：解析 DSLite 输出里的百分比（擦除/烧写阶段）
            var m = Regex.Match(e.Data, @"(\d{1,3})\s*%");
            if (m.Success && int.TryParse(m.Groups[1].Value, out int v) && v >= lastPct && v <= 100)
            {
                lastPct = v;
                Progress?.Invoke(v, e.Data.Trim());
            }
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            lock (outBuf) outBuf.AppendLine(e.Data);
            Output?.Invoke(e.Data);
        };

        if (!proc.Start())
            throw new InvalidOperationException($"无法启动 dslite: {dslite}");
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        // 等待退出（带超时/取消）
        var timeoutWatch = Stopwatch.StartNew();
        while (!proc.HasExited)
        {
            if (ct.IsCancellationRequested)
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* 忽略 */ }
                throw new OperationCanceledException(ct);
            }
            if (_cfg.TimeoutSeconds > 0 && timeoutWatch.Elapsed.TotalSeconds > _cfg.TimeoutSeconds)
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* 忽略 */ }
                throw new TimeoutException($"烧写超时（>{_cfg.TimeoutSeconds}s），进程已终止");
            }
            await Task.Delay(100, CancellationToken.None);
        }
        await Task.Delay(200); // 等待输出排空
        return proc.ExitCode;
    }

    private static string Sha256File(string path)
    {
        using var fs = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(fs))[..16].ToLowerInvariant();
    }

    public void Dispose() => _ = CloseAsync();
}
