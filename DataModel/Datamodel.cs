namespace DataModel
{
    public class EquipmentModel
    {
        public int EquipmentID { get; set; }
        public string? DeviceNumber { get; set; }
        public string? CanType { get; set; }
        public string? DeviceIP { get; set; }
        public string? DevicePort { get; set; }
        public int DeviceIndex { get; set; }
        public int CanIndex { get; set; }
        public int ACNumber { get; set; }
        public int DCNumber { get; set; }
        public string? CommunicationProtocols { get; set; }
        public string? Whether { get; set; }
    }
    public class MessageInfo
    {
        public long MessageID { get; set; }
        public int Orders { get; set; }
        public string CANID { get; set; }
        public string FrameType { get; set; }
        public string MessageName { get; set; }
        public int DataLength { get; set; }
    }
    public class SignalInfo
    {
        public long SignalID { get; set; }
        public int Orders { get; set; }
        public string SignalName { get; set; }
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
}
