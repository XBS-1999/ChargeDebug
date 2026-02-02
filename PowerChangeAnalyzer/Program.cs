using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace PowerChangeAnalyzer
{
    class Program
    {
        static void Main(string[] args)
        {
            // 读取CSV文件内容
            string csvContent = File.ReadAllText("ac侧运行数据.csv");

            // 解析CSV数据
            List<PowerRecord> records = ParseCsvData(csvContent);

            // 按时间排序（确保按时间顺序处理）
            records = records.OrderBy(r => r.Timestamp).ToList();

            // 计算相邻秒之间的功率变化
            List<PowerChange> changes = CalculatePowerChanges(records);

            // 找出最大变化量（绝对值）
            PowerChange maxChange = changes.OrderByDescending(c => Math.Abs(c.ChangeAmount)).First();

            // 输出结果
            Console.WriteLine("=== 功率变化分析结果 ===");
            Console.WriteLine($"数据总行数: {records.Count}");
            Console.WriteLine($"时间范围: {records.First().Timestamp:yyyy-MM-dd HH:mm:ss} 到 {records.Last().Timestamp:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine();
            Console.WriteLine("=== 1秒内功率变化最大值 ===");
            Console.WriteLine($"最大变化量: {Math.Abs(maxChange.ChangeAmount):F2} KW");
            Console.WriteLine($"发生时间: {maxChange.StartTime:yyyy-MM-dd HH:mm:ss} → {maxChange.EndTime:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"起始功率: {maxChange.StartPower:F2} KW");
            Console.WriteLine($"结束功率: {maxChange.EndPower:F2} KW");
            Console.WriteLine($"变化量: {maxChange.ChangeAmount:F2} KW");

            // 输出前10个最大变化
            Console.WriteLine();
            Console.WriteLine("=== 前10个最大功率变化 ===");
            var top10Changes = changes
                .OrderByDescending(c => Math.Abs(c.ChangeAmount))
                .Take(10)
                .ToList();

            for (int i = 0; i < top10Changes.Count; i++)
            {
                var change = top10Changes[i];
                Console.WriteLine($"{i + 1}. 变化量: {Math.Abs(change.ChangeAmount):F2} KW, 时间: {change.StartTime:yyyy-MM-dd HH:mm:ss} → {change.EndTime:yyyy-MM-dd HH:mm:ss}, " +
                                 $"功率: {change.StartPower:F2} → {change.EndPower:F2}");
            }

            // 统计信息
            Console.WriteLine();
            Console.WriteLine("=== 统计信息 ===");
            Console.WriteLine($"平均变化量: {changes.Average(c => Math.Abs(c.ChangeAmount)):F2} KW");
            Console.WriteLine($"最大正向变化: {changes.Max(c => c.ChangeAmount):F2} KW");
            Console.WriteLine($"最大负向变化: {changes.Min(c => c.ChangeAmount):F2} KW");
            Console.WriteLine($"功率范围: {records.Min(r => r.TotalActivePower):F2} 到 {records.Max(r => r.TotalActivePower):F2} KW");

            Console.ReadLine();
        }

        static List<PowerRecord> ParseCsvData(string csvContent)
        {
            var records = new List<PowerRecord>();
            var lines = csvContent.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            // 跳过标题行
            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                // 移除引号并分割
                var parts = line.Replace("\"", "").Split(',');

                if (parts.Length >= 3)
                {
                    var record = new PowerRecord
                    {
                        //WarehouseNum = int.Parse(parts[0]),
                        Timestamp = DateTime.ParseExact(parts[0], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                        TotalActivePower = double.Parse(parts[1], CultureInfo.InvariantCulture),
                        ChannelCount = int.Parse(parts[2])
                    };
                    records.Add(record);
                }
            }

            return records;
        }

        static List<PowerChange> CalculatePowerChanges(List<PowerRecord> records)
        {
            var changes = new List<PowerChange>();

            for (int i = 1; i < records.Count; i++)
            {
                var prev = records[i - 1];
                var curr = records[i];

                // 检查是否为连续的秒数（相差约1秒）
                var timeDiff = (curr.Timestamp - prev.Timestamp).TotalSeconds;

                // 如果时间差接近1秒（允许微小误差），则计算变化
                if (Math.Abs(timeDiff - 1) < 0.1)
                {
                    var change = new PowerChange
                    {
                        StartTime = prev.Timestamp,
                        EndTime = curr.Timestamp,
                        StartPower = prev.TotalActivePower,
                        EndPower = curr.TotalActivePower,
                        ChangeAmount = curr.TotalActivePower - prev.TotalActivePower
                    };
                    changes.Add(change);
                }
            }

            return changes;
        }
    }

    class PowerRecord
    {
        public int WarehouseNum { get; set; }
        public DateTime Timestamp { get; set; }
        public double TotalActivePower { get; set; }
        public int ChannelCount { get; set; }
    }

    class PowerChange
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public double StartPower { get; set; }
        public double EndPower { get; set; }
        public double ChangeAmount { get; set; }
    }
}