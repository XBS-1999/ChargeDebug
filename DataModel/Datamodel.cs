#pragma warning disable
namespace DataModel
{
    public class EquipmentModel
    {
        public int EquipmentID { get; set; }
        public string? DeviceNumber { get; set; }
        public string? DeviceName { get; set; }
        public string? DeviceType { get; set; }
        public string? CanType { get; set; }
        public string? DeviceIP { get; set; }
        public string? DevicePort { get; set; }
        public int? DeviceIndex { get; set; }
        public int? CanIndex { get; set; }
        public int? ACNumber { get; set; }
        public string? ACAddress { get; set; }
        public int? DCNumber { get; set; }
        public string? DCAddress { get; set; }
        public string? CommunicationProtocols { get; set; }
        public string? Whether { get; set; }
        public string? ComPort { get; set; }
        public string? BaudRate { get; set; }
        public string? DataBits { get; set; }
        public string? Parity { get; set; }
        public string? StopBits { get; set; }
        public string? FlowControl { get; set; }
    }
    public class MessageInfo
    {
        public long MessageID { get; set; }
        public int Orders { get; set; }
        public string? CANID { get; set; }
        public string? FrameType { get; set; }
        public string? MessageName { get; set; }
        public int DataLength { get; set; }
    }

    public class SignalInfo
    {
        public long SignalID { get; set; }
        public int Orders { get; set; }
        public string SignalName { get; set; }
        public string MessageName { get; set; }
        public string MultiplexSignals { get; set; }
        public string SystemName { get; set; }
        public string Unit { get; set; }
        public string CANID { get; set; }
        public int StartBit { get; set; }
        public int Length { get; set; }
        public string? ByteOrder { get; set; }
        public string? Signed { get; set; }
        public decimal Factor { get; set; }
        public decimal Offset { get; set; }
        public string? MinMax { get; set; }
        // 新增复用信号列表
        public List<ReuseSignal> ReuseSignals { get; set; } = new List<ReuseSignal>();
    }

    public class ReuseSignal
    {
        public string? Value { get; set; }
        public string? Description { get; set; }
        public int Orders { get; set; }
    }

    public class FaultSignals
    {
        public string Signalname { get; set; }
        public int Startbit { get; set; }
        public int Length { get; set; }
        public string? ByteOrder { get; set; }
        public string? Signed { get; set; }
        public decimal Factor { get; set; }
        public decimal Offset { get; set; }
    }

    public class FaultRecordingSignals
    {
        public string SignalName { get; set; }
        public string CANID { get; set; }
        public int StartBit { get; set; }
        public int Length { get; set; }
        public string? ByteOrder { get; set; }
        public string? Signed { get; set; }
        public decimal Factor { get; set; }
        public decimal Offset { get; set; }
        public string Unit { get; set; }
    }



    /// <summary>
    /// Modbus信号信息类
    /// </summary>
    public class ModbusSignal
    {
        public string? DeviceName { get; set; }
        public long SignalID { get; set; }
        public long DbcFileId { get; set; }
        public string? SignalName { get; set; }
        public string? CorrespondenceAddress { get; set; }
        public string? FunctionCode { get; set; }
        public string? RegisterAddress { get; set; }
        public int RegisterCount { get; set; }
        public string? SystemVariableName { get; set; }
        public string? Unit { get; set; }
        public string? ByteOrder { get; set; }
        public string? Signed { get; set; }
        public double Factor { get; set; }
        public double Offset { get; set; }
        public string? ValueRange { get; set; }
        public int Orders { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }
    }

    public class FaultRecord
    {
        public string Passage { get; set; }
        public string State { get; set; }
    }


    public class CalibrationSignals
    {
        public long SignalID { get; set; }
        public string? DeviceName { get; set; }
        public string? SignalName { get; set; }
        public string? SignalType { get; set; }
        public string? CalibrationSignal { get; set; }
        public int ReadTime { get; set; }
        public string? RatingVoltageCurrent { get; set; }
        public int CalibrationNumber { get; set; }
        public int Orders { get; set; }
        public string? ScaleFactor { get; set; }
        public string? ZeroFactor { get; set; }
        public string? CalibrationAccuracy { get; set; }
    }

    /// <summary>
    /// 校准点类
    /// </summary>
    public class CalibrationPoint
    {
        /// <summary>
        /// 电压值
        /// </summary>
        public double Voltage { get; set; }

        /// <summary>
        /// 读取时间（毫秒）
        /// </summary>
        public int ReadTimeMs { get; set; }

        /// <summary>
        /// 信号信息
        /// </summary>
        public SignalInfo SignalInfo { get; set; }

        /// <summary>
        /// 设备名称
        /// </summary>
        public string DeviceName { get; set; }

        /// <summary>
        /// 信号名称
        /// </summary>
        public string SignalName { get; set; }

        /// <summary>
        /// 校准信号
        /// </summary>
        public string CalibrationSignal { get; set; }

        /// <summary>
        /// 额定电压
        /// </summary>
        //public string RatedVoltage { get; set; }

        /// <summary>
        /// 精度范围
        /// </summary>
        //public string PrecisionRange { get; set; }
    }

    public class FileInfoDto
    {
        public int FileID { get; set; }
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public long FileSize { get; set; }
        public DateTime CreateTime { get; set; }
        public int RecordCount { get; set; }
    }

    #region 记录模型类

    /// <summary>
    /// 文件时间信息类
    /// </summary>
    public class FileTimeInfo
    {
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public long FileSize { get; set; }
    }

    /// <summary>
    /// 文件组信息类
    /// </summary>
    public class FileGroupInfo
    {
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public long FileSize { get; set; }
    }

    public class Template
    {
        public string Name { get; set; }
        public string ChineseName { get; set; }
        public string Field_Type { get; set; }
        public string Unit { get; set; }
        public string Type_Table { get; set; }
        public string Type_Chart { get; set; }
    }

    public class Cal7Record
    {
        public DateTime CreateTime { get; set; }
        public int? ChannelNum { get; set; }
        public decimal? Power { get; set; }
        public double? ChargeEnergy { get; set; }
        public double? DischargeEnergy { get; set; }
        public int? DeviceStatus { get; set; }
        public string KeyValue { get; set; }
        public int? Sign {  get; set; }

    }

    public class Cal6Record
    {
        public DateTime CreateTime { get; set; }
        public string CurrentDate { get; set; }
        public string StartTime { get; set; }
        public string EndTime { get; set; }
        public double? MaxFrequencyPower { get; set; }
        public double? Paclm5 { get; set; }
        public double? Paclm6 { get; set; }
        public double? Paclm1 { get; set; }
        public double? Paclm2 { get; set; }
        public double? SocMin { get; set; }
        public double? SocMax { get; set; }
        public double? KP { get; set; }
        public double? LimitChargeCurrent { get; set; }
        public double? LimitDischargeCurrent { get; set; }
        public int? Sign { get; set; }
    }

    public class Cal5Record
    {
        public DateTime CreateTime { get; set; }
        public int? ChannelNum { get; set; }
        public double? SOH { get; set; }
        public double? Current { get; set; }
        public decimal? Power { get; set; }
        public string EMSStatus { get; set; }
        public double? ChargeEnergy { get; set; }
        public double? Voltage { get; set; }
        public int? DeviceStatus { get; set; }
        public double? SOC { get; set; }
        public double? DischargeEnergy { get; set; }
        public string EMSMode { get; set; }
        public string KeyValue { get; set; }
        public int? Sign { get; set; }
    }

    public class Cal2Record
    {
        public DateTime CreateTime { get; set; }
        public string Condition { get; set; }
        public double? Pac0 { get; set; }
        public double? PreviewPAC { get; set; }
        public double? PacMin { get; set; }
        public double? PacMax { get; set; }
        public double? KP { get; set; }
        public double? MaxFrequencyPower { get; set; }
        public double? Pace0 { get; set; }
        public int? Sign { get; set; }
    }

    public class Cal1Record
    {
        public DateTime CreateTime { get; set; }
        public string Condition { get; set; }
        public double? Pac0 { get; set; }
        public double? PreviewPAC { get; set; }
        public double? Csoc { get; set; }
        public double? Paclm1 { get; set; }
        public double? Paclm2 { get; set; }
        public double? Paclm5 { get; set; }
        public double? Paclm6 { get; set; }
        public double? KP { get; set; }
        public double? SocMin { get; set; }
        public double? SocMax { get; set; }
        public double? MaxFrequencyPower { get; set; }
        public int? Sign { get; set; }
    }

    public class Cal3Record
    {
        public DateTime CreateTime { get; set; }
        public string Condition { get; set; }
        public double? Pc1n { get; set; }
        public double? TotalScale { get; set; }
        public double? CurrentValue { get; set; }
        public double? SOC { get; set; }
        public int? Sign { get; set; }
    }

    public class Cal4Record
    {
        public DateTime CreateTime { get; set; }
        public string Condition { get; set; }
        public string DataType { get; set; }
        public string JsonData { get; set; }
        public string Receivers { get; set; }
        public int? Sign { get; set; }
    }

    public class NoneRecord
    {
        public DateTime CreateTime { get; set; }
        public string RecordContent { get; set; }
        public string SequenceNumber { get; set; }
        public int? Sign { get; set; }
    }

    #endregion

    public class TestProjectModel
    {
        public int Id { get; set; }
        public int ProjectId { get; set; }
        public string ProjectName { get; set; }
        public double TestVoltage { get; set; }
        public double TestTime { get; set; }
        public double RampUpTime { get; set; }
        public double RampDownTime { get; set; }
        public string CurrentLimit { get; set; }
        public string ResistanceLimit { get; set; }
        public DateTime? CreateTime { get; set; }
        public DateTime? UpdateTime { get; set; }
    }


    public class DeviceData
    {
        public DateTime Time { get; set; }
        public float Voltage { get; set; } // 电压
        public float Current { get; set; } // 电流
        public float Temp { get; set; }    // 温度
        public float Power { get; set; }   // 功率
        public bool IsFault { get; set; }
        public string Status { get; set; } = "正常";
    }

    public class FaultRecordAL
    {
        public int Id { get; set; }
        public DateTime CreateTime { get; set; }
        public float Voltage { get; set; }
        public float Current { get; set; }
        public float Temp { get; set; }
        public string AiResult { get; set; } = string.Empty;
        public bool IsLearned { get; set; } // 是否已学习
    }
}
