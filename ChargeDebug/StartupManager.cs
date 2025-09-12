using ChargeDebug.Service;
using DataModel;
using Log;
using ChargeDebug.Form;
using DevExpress.XtraEditors;
using DocumentFormat.OpenXml.Spreadsheet;
using DevExpress.XtraRichEdit.Fields;
using System.Diagnostics;

namespace ChargeDebug
{
    /// <summary>
    /// 设备启动管理器 - 管理多个设备的启动过程
    /// </summary>
    public class StartupManager
    {
        private readonly EquipmentModel _equipment;
        private readonly string _title;

        public StartupManager(EquipmentModel equipment, string title)
        {
            _equipment = equipment;
            _title = title;
        }

        /// <summary>
        /// 检查设备状态是否允许启动
        /// </summary>
        public int CheckDeviceStatus(uint acRunStatus, uint dcRunStatus)
        {
            // 检查设备运行状态 0x00-待机 0x01-启动过程中 0x02-运行 0x03-停机过程中 0xFF-故障
            if ((acRunStatus == 0x02) && (dcRunStatus == 0x02))
            {
                return 0x02;
            }
            else if ((acRunStatus == 0x00) && (dcRunStatus == 0x00))
            {
                return 0x00;
            }
            else
            {
                if ((acRunStatus == 0xFF) || (dcRunStatus == 0xFF))
                {
                    return 0xFF;
                }
                else if ((acRunStatus == 0x01) || (dcRunStatus == 0x01))
                {
                    return 0x01;
                }
                else
                {
                    return 0x03;
                }
            }
        }

        /// <summary>
        /// 启动设备
        /// </summary>
        public async Task<bool> StartDeviceAsync(ConfigurationData configData)
        {
            try
            {
                // 步骤1: 发送保护参数52YCC
                bool protectparameters = await SendProtectionParams52YCC(configData);
                if (!protectparameters)
                {
                    return false;
                }

                // 步骤2: 发送保护参数62YCC
                bool protectparameters1 = await SendProtectionParams62YCC(configData);
                if (!protectparameters1)
                {
                    return false;
                }

                // 步骤3: 发送控制参数32YCC
                bool controlparameters = await SendControlParams32YCC(configData);
                if (!controlparameters)
                {
                    return false;
                }

                // 步骤3: 发送工步参数22YCC
                bool processparameters = await SendStepParams22YCC(configData);
                if (!processparameters)
                {
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                //XtraMessageBox.Show($"设备启动失败:{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 发送保护参数52YCC
        /// </summary>
        private async Task<bool> SendProtectionParams52YCC(ConfigurationData configData)
        {
            try
            {
                // 通道号 (从标题中提取，如"通道1" -> 1)
                uint channelNum = Convert.ToUInt32(_title.Substring(_title.Length - 1, 1));
                //发送CANID
                uint sendCanId = 0x520CC + (channelNum - 1) * 0x100;
                //接收CANID
                uint receiveCanId = 0x5CC20 + (channelNum - 1) * 0x01;

                // 构造保护参数数据
                byte[] data = new byte[8];

                ushort overVoltageValue = (ushort)(Convert.ToInt32(configData.OverVoltage) * 10);
                data[0] = (byte)(overVoltageValue & 0xFF);        // 低字节
                data[1] = (byte)((overVoltageValue >> 8) & 0xFF); // 高字节

                ushort underVoltageValue = (ushort)(Convert.ToInt32(configData.UnderVoltage) * 10);
                data[2] = (byte)(underVoltageValue & 0xFF);       // 低字节
                data[3] = (byte)((underVoltageValue >> 8) & 0xFF);// 高字节

                ushort overCurrentValue = (ushort)(Convert.ToInt32(configData.OverCurrent) * 10);
                data[4] = (byte)(overCurrentValue & 0xFF);        // 低字节
                data[5] = (byte)((overCurrentValue >> 8) & 0xFF); // 高字节

                ushort underCurrentValue = (ushort)(Convert.ToInt32(configData.UnderCurrent) * 10);
                data[6] = (byte)(underCurrentValue & 0xFF);       // 低字节
                data[7] = (byte)((underCurrentValue >> 8) & 0xFF);// 高字节

                // 构造通道键
                string channelKey = CANManager.GetChannelKey(_equipment.DeviceIndex, _equipment.CanIndex);

                int number = 0;
                while (number < 3)
                {
                    CANManager.Instance.SendCommand
                    (
                        _equipment.DeviceIndex,
                        _equipment.CanIndex,
                        sendCanId,
                        data
                    );
                    
                    var response = await CANManager.Instance.ReceiveFrameAsync(channelKey, receiveCanId, 20);

                    // 验证数据是否写入
                    if (!data.SequenceEqual(response.data))
                    {
                        number ++;
                    }
                    else
                    {
                        break;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                throw new Exception($"发送保护参数52YCC失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 发送保护参数62YCC
        /// </summary>
        private async Task<bool> SendProtectionParams62YCC(ConfigurationData configData)
        {
            try
            {
                // 通道号 (从标题中提取，如"通道1" -> 1)
                uint channelNum = Convert.ToUInt32(_title.Substring(_title.Length - 1, 1));
                //发送CANID
                uint sendCanId = 0x620CC + (channelNum - 1) * 0x100;
                //接收CANID
                uint receiveCanId = 0x6CC20 + (channelNum - 1) * 0x01;

                // 构造保护参数数据
                byte[] data = new byte[8];

                ushort overPowerValue = (ushort)(Convert.ToInt32(configData.OverPower) * 10);
                data[0] = (byte)(overPowerValue & 0xFF);        // 低字节
                data[1] = (byte)((overPowerValue >> 8) & 0xFF); // 高字节

                ushort underPowerValue = (ushort)(Convert.ToInt32(configData.UnderPower) * 10);
                data[2] = (byte)(underPowerValue & 0xFF);       // 低字节
                data[3] = (byte)((underPowerValue >> 8) & 0xFF);// 高字节

                data[4] = 0x00;      
                data[5] = 0x00; 
                data[6] = 0x00; 
                data[7] = 0x00;

                // 构造通道键
                string channelKey = CANManager.GetChannelKey(_equipment.DeviceIndex, _equipment.CanIndex);

                int number = 0;
                while (number < 3)
                {
                    CANManager.Instance.SendCommand
                    (
                        _equipment.DeviceIndex,
                        _equipment.CanIndex,
                        sendCanId,
                        data
                    );

                    var response = await CANManager.Instance.ReceiveFrameAsync(channelKey, receiveCanId, 20);

                    // 验证数据是否写入
                    if (!data.SequenceEqual(response.data))
                    {
                        number++;
                    }
                    else
                    {
                        break;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                throw new Exception($"发送保护参数62YCC失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 发送控制参数32YCC
        /// </summary>
        private async Task<bool> SendControlParams32YCC(ConfigurationData configData)
        {
            try
            {
                // 通道号 (从标题中提取，如"通道1" -> 1)
                uint channelNum = Convert.ToUInt32(_title.Substring(_title.Length - 1, 1));
                //发送CANID
                uint sendCanId = 0x320CC + (channelNum - 1) * 0x100;
                //接收CANID
                uint receiveCanId = 0x3CC20 + (channelNum - 1) * 0x01;

                // 构造保护参数数据
                byte[] data = new byte[8];

                switch (configData.WorkingMode)
                {
                    case "恒流充电":
                        if (configData.DynamicParameters.TryGetValue("ConstantCurrentCharge_Current", out string? currentValueStr) &&
                            double.TryParse(currentValueStr, out double currentValue))
                        {
                            // 控制参数1
                            int controlparameters1 = (int)(currentValue * 100);
                            data[0] = (byte)(controlparameters1 & 0xFF);           // 最低有效字节
                            data[1] = (byte)((controlparameters1 >> 8) & 0xFF);    // 次低有效字节
                            data[2] = (byte)((controlparameters1 >> 16) & 0xFF);   // 次高有效字节
                            data[3] = (byte)((controlparameters1 >> 24) & 0xFF);   // 最高有效字节

                            if (configData.DynamicParameters.TryGetValue("ConstantCurrentCharge_VoltageLimit", out string? voltageValueStr) &&
                                double.TryParse(voltageValueStr, out double voltageValue))
                            {
                                // 控制参数2
                                int controlparameters2 = (int)(voltageValue * 100);
                                data[4] = (byte)(controlparameters2 & 0xFF);
                                data[5] = (byte)((controlparameters2 >> 8) & 0xFF);
                                data[6] = (byte)((controlparameters2 >> 16) & 0xFF);
                                data[7] = (byte)((controlparameters2 >> 24) & 0xFF);
                            }
                        }
                        break;

                    case "恒流放电":
                        if (configData.DynamicParameters.TryGetValue("ConstantCurrentDischarge_Current", out string? discurrentValueStr) &&
                            double.TryParse(discurrentValueStr, out double discurrentValue))
                        {
                            // 控制参数1
                            int controlparameters1 = (int)(discurrentValue * 100);
                            data[0] = (byte)(controlparameters1 & 0xFF);           // 最低有效字节
                            data[1] = (byte)((controlparameters1 >> 8) & 0xFF);    // 次低有效字节
                            data[2] = (byte)((controlparameters1 >> 16) & 0xFF);   // 次高有效字节
                            data[3] = (byte)((controlparameters1 >> 24) & 0xFF);   // 最高有效字节

                            if (configData.DynamicParameters.TryGetValue("ConstantCurrentCharge_VoltageLimit", out string? disvoltageValueStr) &&
                                double.TryParse(disvoltageValueStr, out double disvoltageValue))
                            {
                                // 控制参数2
                                int controlparameters2 = (int)(disvoltageValue * 100);
                                data[4] = (byte)(controlparameters2 & 0xFF);
                                data[5] = (byte)((controlparameters2 >> 8) & 0xFF);
                                data[6] = (byte)((controlparameters2 >> 16) & 0xFF);
                                data[7] = (byte)((controlparameters2 >> 24) & 0xFF);
                            }
                        }
                        break;

                    case "恒压充电":
                        if (configData.DynamicParameters.TryGetValue("ConstantVoltageCharge_CurrentLimit", out string? limitchargingcurrent) &&
                            double.TryParse(limitchargingcurrent, out double setlimitchargingcurrent))
                        {
                            // 控制参数1
                            int controlparameters1 = (int)(setlimitchargingcurrent * 100);
                            data[0] = (byte)(controlparameters1 & 0xFF);           // 最低有效字节
                            data[1] = (byte)((controlparameters1 >> 8) & 0xFF);    // 次低有效字节
                            data[2] = (byte)((controlparameters1 >> 16) & 0xFF);   // 次高有效字节
                            data[3] = (byte)((controlparameters1 >> 24) & 0xFF);   // 最高有效字节

                            if (configData.DynamicParameters.TryGetValue("ConstantVoltageCharge_Voltage", out string? voltage) &&
                                double.TryParse(voltage, out double setvoltage))
                            {
                                // 控制参数2
                                uint controlparameters2 = (uint)(setvoltage * 100);
                                data[4] = (byte)(controlparameters2 & 0xFF);
                                data[5] = (byte)((controlparameters2 >> 8) & 0xFF);
                                data[6] = (byte)((controlparameters2 >> 16) & 0xFF);
                                data[7] = (byte)((controlparameters2 >> 24) & 0xFF);
                            }
                        }
                        break;

                    case "恒压放电":
                        if (configData.DynamicParameters.TryGetValue("ConstantVoltageDischarge_CurrentLimit", out string? limitdischargingcurrent) &&
                            double.TryParse(limitdischargingcurrent, out double setlimitdischargingcurrent))
                        {
                            // 控制参数1
                            int controlparameters1 = (int)(setlimitdischargingcurrent * 100);
                            data[0] = (byte)(controlparameters1 & 0xFF);           // 最低有效字节
                            data[1] = (byte)((controlparameters1 >> 8) & 0xFF);    // 次低有效字节
                            data[2] = (byte)((controlparameters1 >> 16) & 0xFF);   // 次高有效字节
                            data[3] = (byte)((controlparameters1 >> 24) & 0xFF);   // 最高有效字节

                            if (configData.DynamicParameters.TryGetValue("ConstantVoltageDischarge_Voltage", out string? voltage) &&
                                double.TryParse(voltage, out double setvoltage))
                            {
                                // 控制参数2
                                int controlparameters2 = (int)(setvoltage * 100);
                                data[4] = (byte)(controlparameters2 & 0xFF);
                                data[5] = (byte)((controlparameters2 >> 8) & 0xFF);
                                data[6] = (byte)((controlparameters2 >> 16) & 0xFF);
                                data[7] = (byte)((controlparameters2 >> 24) & 0xFF);
                            }
                        }
                        break;

                    case "恒功率充电":
                        if (configData.DynamicParameters.TryGetValue("ConstantPowerCharge_Power", out string? power) &&
                            double.TryParse(power, out double setpower))
                        {
                            // 控制参数1
                            int controlparameters1 = (int)(setpower * 100);
                            data[0] = (byte)(controlparameters1 & 0xFF);           // 最低有效字节
                            data[1] = (byte)((controlparameters1 >> 8) & 0xFF);    // 次低有效字节
                            data[2] = (byte)((controlparameters1 >> 16) & 0xFF);   // 次高有效字节
                            data[3] = (byte)((controlparameters1 >> 24) & 0xFF);   // 最高有效字节

                            if (configData.DynamicParameters.TryGetValue("ConstantPowerCharge_CurrentLimit", out string? limitcurrent) &&
                                double.TryParse(limitcurrent, out double setlimitcurrent))
                            {
                                // 控制参数2
                                int controlparameters2 = (int)(setlimitcurrent * 100);
                                data[4] = (byte)(controlparameters2 & 0xFF);
                                data[5] = (byte)((controlparameters2 >> 8) & 0xFF);
                                data[6] = (byte)((controlparameters2 >> 16) & 0xFF);
                                data[7] = (byte)((controlparameters2 >> 24) & 0xFF);
                            }
                        }
                        break;

                    case "恒功率放电":
                        if (configData.DynamicParameters.TryGetValue("ConstantPowerDischarge_Power", out string? dispower) &&
                            double.TryParse(dispower, out double setdispower))
                        {
                            // 控制参数1
                            int controlparameters1 = (int)(setdispower * 100);
                            data[0] = (byte)(controlparameters1 & 0xFF);           // 最低有效字节
                            data[1] = (byte)((controlparameters1 >> 8) & 0xFF);    // 次低有效字节
                            data[2] = (byte)((controlparameters1 >> 16) & 0xFF);   // 次高有效字节
                            data[3] = (byte)((controlparameters1 >> 24) & 0xFF);   // 最高有效字节

                            if (configData.DynamicParameters.TryGetValue("ConstantPowerDischarge_CurrentLimit", out string? limitdiscurrent) &&
                                double.TryParse(limitdiscurrent, out double setlimitdiscurrent))
                            {
                                // 控制参数2
                                int controlparameters2 = (int)(setlimitdiscurrent * 100);
                                data[4] = (byte)(controlparameters2 & 0xFF);
                                data[5] = (byte)((controlparameters2 >> 8) & 0xFF);
                                data[6] = (byte)((controlparameters2 >> 16) & 0xFF);
                                data[7] = (byte)((controlparameters2 >> 24) & 0xFF);
                            }
                        }
                        break;

                    default:
                        return true;
                }
                
                // 构造通道键
                string channelKey = CANManager.GetChannelKey(_equipment.DeviceIndex, _equipment.CanIndex);

                int number = 0;
                while (number < 3)
                {
                    CANManager.Instance.SendCommand
                    (
                        _equipment.DeviceIndex,
                        _equipment.CanIndex,
                        sendCanId,
                        data
                    );

                    var response = await CANManager.Instance.ReceiveFrameAsync(channelKey, receiveCanId, 20);

                    // 验证数据是否写入
                    if (!data.SequenceEqual(response.data))
                    {
                        number++;
                    }
                    else
                    {
                        break;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                throw new Exception($"发送控制参数32YCC失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 发送工步参数22YCC
        /// </summary>
        private async Task<bool> SendStepParams22YCC(ConfigurationData configData)
        {
            try
            {
                // 通道号 (从标题中提取，如"通道1" -> 1)
                uint channelNum = Convert.ToUInt32(_title.Substring(_title.Length - 1, 1));
                //发送CANID
                uint sendCanId = 0x220CC + (channelNum - 1) * 0x100;
                //接收CANID
                uint receiveCanId = 0x2CC20 + (channelNum - 1) * 0x01;

                // 构造数据
                byte[] data = new byte[8];

                switch (configData.WorkingMode)
                {
                    case "恒流充电":
                        data[0] = 0x03;
                        break;
                    case "恒流放电":
                        data[0] = 0x23;
                        break;
                    case "恒压充电":
                        data[0] = 0x02;
                        break;
                    case "恒压放电":
                        data[0] = 0x22;
                        break;
                    case "恒功率充电":
                        data[0] = 0x01;
                        break;
                    case "恒功率放电":
                        data[0] = 0x21;
                        break;
                    case "搁置":
                        data[0] = 0x05;
                        break;
                    case "静置":
                        data[0] = 0x06;
                        break;
                    case "停机":
                        data[0] = 0x00;
                        break;

                    default:
                        data[0] = 0x00;
                        break;
                }

                data[1] = 0x00;
                data[2] = 0x00;
                data[3] = 0x00;
                data[4] = 0x00;
                data[5] = 0x00;
                data[6] = 0x00;
                data[7] = 0x00;

                // 构造通道键
                string channelKey = CANManager.GetChannelKey(_equipment.DeviceIndex, _equipment.CanIndex);

                int number = 0;
                while (number < 3)
                {
                    CANManager.Instance.SendCommand
                    (
                        _equipment.DeviceIndex,
                        _equipment.CanIndex,
                        sendCanId,
                        data
                    );

                    var response = await CANManager.Instance.ReceiveFrameAsync(channelKey, receiveCanId, 20);

                    // 验证数据是否写入
                    if (!data.SequenceEqual(response.data))
                    {
                        number++;
                    }
                    else
                    {
                        break;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                throw new Exception($"发送工步参数22YCC失败: {ex.Message}");
            }
        }

        public async Task<bool> StopDeviceAsync()
        {
            try
            {
                // 通道号 (从标题中提取，如"通道1" -> 1)
                uint channelNum = Convert.ToUInt32(_title.Substring(_title.Length - 1, 1));
                //发送CANID
                uint sendCanId = 0x220CC + (channelNum - 1) * 0x100;
                //接收CANID
                uint receiveCanId = 0x2CC20 + (channelNum - 1) * 0x01;

                // 构造数据
                byte[] data = new byte[8];
                data[0] = 0x00;
                data[1] = 0x00;
                data[2] = 0x00;
                data[3] = 0x00;
                data[4] = 0x00;
                data[5] = 0x00;
                data[6] = 0x00;
                data[7] = 0x00;

                // 构造通道键
                string channelKey = CANManager.GetChannelKey(_equipment.DeviceIndex, _equipment.CanIndex);
                
                int number = 0;
                while (number < 3)
                {
                    CANManager.Instance.SendCommand
                    (
                        _equipment.DeviceIndex,
                        _equipment.CanIndex,
                        sendCanId,
                        data
                    );

                    var response = await CANManager.Instance.ReceiveFrameAsync(channelKey, receiveCanId, 20);

                    // 验证数据是否写入
                    if (!data.SequenceEqual(response.data))
                    {
                        number++;
                    }
                    else
                    {
                        break;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                throw new Exception($"发送停机指令22YCC失败: {ex.Message}");
            }
        }
    }
}