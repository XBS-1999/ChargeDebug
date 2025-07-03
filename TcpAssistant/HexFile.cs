using System.IO;

namespace TcpAssistant
{
    public class HexFileData
    {
        public List<HexRecord> Records { get; set; } // 存储所有数据记录
        public uint MinAddress { get; set; }         // 最小地址
        public uint MaxAddress { get; set; }         // 最大地址
                                                     // 添加块数据列表
        public List<DataBlock> Blocks { get; set; } = new List<DataBlock>();
    }

    public class HexRecord
    {
        public uint Address { get; }
        public byte[] Data { get; }
        public int Length => Data.Length;

        public HexRecord(uint address, byte[] data)
        {
            Address = address;
            Data = data;
        }
    }

    public class DataBlock
    {
        public uint StartAddress { get; set; }
        public byte[] Data { get; set; }
        public int BlockIndex { get; set; }
    }

    public class HexFile
    {
        public static HexFileData ParseHexFile(string filePath, string firmwareModel)
        {
            List<HexRecord> records = new List<HexRecord>();
            uint upperAddress = 0;
            uint minAddr = uint.MaxValue;
            uint maxAddr = 0;

            // 第一遍：收集记录并确定地址范围
            foreach (string line in File.ReadAllLines(filePath))
            {
                if (string.IsNullOrWhiteSpace(line) || line[0] != ':')
                    continue;

                byte[] bytes = HexStringToBytes(line.Substring(1));
                byte dataLength = bytes[0];
                ushort address = (ushort)((bytes[1] << 8) | bytes[2]);
                byte recordType = bytes[3];
                byte[] data = new byte[dataLength];
                Array.Copy(bytes, 4, data, 0, dataLength);
                byte checksum = bytes[4 + dataLength];

                if (CalculateChecksum(bytes, 0, bytes.Length - 1) != checksum)
                    throw new Exception($"校验和错误: {line}");

                switch (recordType)
                {
                    case 0x00: // 数据记录
                        uint fullAddress = (upperAddress << 16) + address;
                        HexRecord record = new HexRecord(fullAddress, data);
                        records.Add(record);
                        minAddr = Math.Min(minAddr, fullAddress);
                        maxAddr = Math.Max(maxAddr, fullAddress + ((uint)dataLength) / 2);
                        break;
                    case 0x04: // 扩展线性地址记录
                        upperAddress = (uint)((data[0] << 8) | data[1]);
                        break;
                    case 0x01: // 文件结束
                        break;
                }
            }

            records.Sort((a, b) => a.Address.CompareTo(b.Address));

            // 第二遍：构建块并填充间隙（按双字节填充0x0000）
            List<DataBlock> blocks = new List<DataBlock>();
            List<byte> currentBlockData = new List<byte>();
            uint currentBlockStartAddress = minAddr;
            uint currentAddress = minAddr;

            foreach (var record in records)
            {
                // 计算当前记录需要多少字节
                int bytesNeeded = record.Data.Length;

                // 如果当前块剩余空间不足，且当前块已有数据
                if (currentBlockData.Count > 0 &&
                    currentBlockData.Count + bytesNeeded > 256)
                {
                    // 完成当前块
                    currentAddress = record.Address;
                    FinalizeCurrentBlock(blocks, ref currentBlockData, ref currentBlockStartAddress, currentAddress);
                }

                // 处理地址间隙（按双字节单位填充0x0000）
                if ((record.Address > currentAddress) && (currentBlockData.Count > 0))
                {
                    uint gapSize = record.Address - currentAddress;
                    for (int i = 0; i < gapSize; i++)
                    {
                        currentBlockData.Add(0x00);
                        currentBlockData.Add(0x00);
                        if (currentBlockData.Count >= 256)
                        {
                            FinalizeCurrentBlock(blocks, ref currentBlockData, ref currentBlockStartAddress, currentAddress + (uint)i + 1);
                        }
                    }
                    currentAddress = record.Address;
                }

                // 添加当前记录数据
                currentBlockData.AddRange(record.Data);
                if(firmwareModel.Substring(0,3) == "ARM")
                {
                    currentAddress += (uint)record.Data.Length;
                }
                else
                {
                    currentAddress += (uint)record.Data.Length / 2;
                }
                
                // 检查是否达到块大小限制（256字节）
                if (currentBlockData.Count >= 256)
                {
                    FinalizeCurrentBlock(blocks, ref currentBlockData, ref currentBlockStartAddress, currentAddress);
                }
            }

            // 添加最后一个块（如果有剩余数据）
            if (currentBlockData.Count > 0)
            {
                FinalizeCurrentBlock(blocks, ref currentBlockData, ref currentBlockStartAddress, currentAddress);
            }

            return new HexFileData
            {
                Records = records,
                Blocks = blocks,
                MinAddress = minAddr,
                MaxAddress = maxAddr
            };
        }

        private static void FinalizeCurrentBlock(List<DataBlock> blocks, ref List<byte> currentData,
            ref uint startAddress, uint nextAddress)
        {
            if (currentData.Count == 0) return;

            // 确保数据大小是8的倍数（调用处已处理，此处为双重保障）
            PadToMultipleOf8(ref currentData);

            blocks.Add(new DataBlock
            {
                StartAddress = startAddress,
                Data = currentData.ToArray(),
                BlockIndex = blocks.Count + 1
            });

            //AppendInfo($"✅ HEX解析成功: 第{blocks.Count}块-" +
            //           $"起始地址: 0x{startAddress:X8}, " +
            //           $"结束地址{nextAddress:X8}, " +
            //           $"共有 {currentData.Count} 个字节"); // 新增块数提示

            // 准备新块
            startAddress = nextAddress;
            currentData.Clear();
        }

        private static void PadToMultipleOf8(ref List<byte> data)
        {
            int remainder = data.Count % 8;
            if (remainder != 0)
            {
                int paddingBytes = 8 - remainder;
                for (int i = 0; i < paddingBytes; i++)
                {
                    data.Add(0x00); // 填充0x00
                }
                //AppendInfo($"填充 {paddingBytes} 字节使块大小对齐到8的倍数");
            }
        }

        public static byte[] HexStringToBytes(string hex)
        {
            int length = hex.Length / 2;
            byte[] bytes = new byte[length];
            for (int i = 0; i < length; i++)
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return bytes;
        }

        private static byte CalculateChecksum(byte[] data, int start, int end)
        {
            byte sum = 0;
            for (int i = start; i < end; i++)
                sum += data[i];
            return (byte)((0x100 - sum) & 0xFF);
        }
    }
}
