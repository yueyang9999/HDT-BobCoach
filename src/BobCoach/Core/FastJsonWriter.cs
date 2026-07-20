using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace BobCoach.Engine
{
    /// <summary>流式写出大型诊断JSON，避免JavaScriptSerializer构造整份大字符串。</summary>
    public static class FastJsonWriter
    {
        public static void Serialize(TextWriter writer, object value)
        {
            var serializer = JsonSerializer.CreateDefault();
            using (var jsonWriter = new JsonTextWriter(writer) { CloseOutput = false })
            {
                serializer.Serialize(jsonWriter, value);
                jsonWriter.Flush();
            }
        }

        public static void Write(string path, object value)
        {
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 65536))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 65536))
                Serialize(writer, value);
        }
    }
}
