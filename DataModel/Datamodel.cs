using System.IO.Ports;

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

    public class FaultRecording
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

    public class CalibrationSignals
    {
        public long SignalID { get; set; }
        public string? DeviceName { get; set; }
        public string? SignalName { get; set; }
        public string? SignalType { get; set; }
        public int ReadTime { get; set; }
        public int RatingVoltageCurrent { get; set; }
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
        /// 额定电压
        /// </summary>
        //public string RatedVoltage { get; set; }

        /// <summary>
        /// 精度范围
        /// </summary>
        //public string PrecisionRange { get; set; }
    }
}
