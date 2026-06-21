using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DataModel;
using RestSharp;
using Newtonsoft.Json;

namespace FaultDiagnosis
{
    public class DoubaoAIService
    {
        private const string API_KEY = "你的豆包API Key";
        private const string URL = "https://ark.cn-beijing.volces.com/api/v3/chat/completions";

        public async Task<string> Diagnose(DeviceData data)
        {
            try
            {
                var client = new RestClient(URL);
                var req = new RestRequest();

                req.AddHeader("Authorization", $"Bearer {API_KEY}");
                req.AddHeader("Content-Type", "application/json");

                var prompt = $"""
                你是大功率充放电设备专业故障诊断工程师。
                当前数据：
                电压：{data.Voltage}V
                电流：{data.Current}A
                温度：{data.Temp}℃
                状态：{data.Status}

                请按格式输出：
                1.故障类型
                2.可能原因
                3.处理方案
                """;

                var body = new
                {
                    model = "doubao-lite-4k",
                    messages = new[] { new { role = "user", content = prompt } },
                    temperature = 0.1f
                };

                req.AddJsonBody(body);
                var res = await client.PostAsync(req);
                var obj = JsonConvert.DeserializeObject<DoubaoResp>(res.Content!);
                return obj?.choices?[0]?.message?.content ?? "诊断失败";
            }
            catch
            {
                return "AI服务异常";
            }
        }
    }

    // 响应结构
    public class DoubaoResp
    {
        public Choice[] choices { get; set; } = Array.Empty<Choice>();
    }
    public class Choice
    {
        public Message message { get; set; } = new Message();
    }
    public class Message
    {
        public string content { get; set; } = string.Empty;
    }
}
