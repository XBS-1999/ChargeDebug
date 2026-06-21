using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using DataModel;

namespace FaultDiagnosis
{
    public class AutoLearnService
    {
        private readonly string _conn = "Data Source=FaultLearn.db";

        public AutoLearnService()
        {
            using var con = new SqliteConnection(_conn);
            con.Open();
            var cmd = new SqliteCommand(@"
            CREATE TABLE IF NOT EXISTS FaultLearn (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Voltage REAL, Current REAL, Temp REAL,
                AiResult TEXT, CreateTime TEXT, IsLearned INTEGER
            )", con);
            cmd.ExecuteNonQuery();
        }

        // 保存故障 → 用于学习
        public void SaveFault(FaultRecordAL record)
        {
            using var con = new SqliteConnection(_conn);
            con.Open();
            var cmd = new SqliteCommand(@"
            INSERT INTO FaultLearn (Voltage,Current,Temp,AiResult,CreateTime,IsLearned)
            VALUES (@v,@c,@t,@a,@time,0)", con);

            cmd.Parameters.AddWithValue("@v", record.Voltage);
            cmd.Parameters.AddWithValue("@c", record.Current);
            cmd.Parameters.AddWithValue("@t", record.Temp);
            cmd.Parameters.AddWithValue("@a", record.AiResult);
            cmd.Parameters.AddWithValue("@time", DateTime.Now.ToString("o"));
            cmd.ExecuteNonQuery();
        }

        // 获取相似故障（自动匹配→自动学习）
        public List<FaultRecordAL> GetSimilarFaults(float v, float c, float t)
        {
            using var con = new SqliteConnection(_conn);
            con.Open();
            var cmd = new SqliteCommand(@"
            SELECT * FROM FaultLearn 
            WHERE ABS(Voltage - @v) < 5 
            AND ABS(Current - @c) < 5 
            AND ABS(Temp - @t) < 5
            ORDER BY Id DESC LIMIT 3", con);

            cmd.Parameters.AddWithValue("@v", v);
            cmd.Parameters.AddWithValue("@c", c);
            cmd.Parameters.AddWithValue("@t", t);

            var list = new List<FaultRecordAL>();
            var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new FaultRecordAL
                {
                    Voltage = (float)reader.GetDouble(1),
                    Current = (float)reader.GetDouble(2),
                    Temp = (float)reader.GetDouble(3),
                    AiResult = reader.GetString(4),
                    IsLearned = reader.GetInt32(6) == 1
                });
            }
            return list;
        }

        // 标记已学习
        public void MarkLearned(int id)
        {
            using var con = new SqliteConnection(_conn);
            con.Open();
            var cmd = new SqliteCommand("UPDATE FaultLearn SET IsLearned=1 WHERE Id=@id", con);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }
    }
}
