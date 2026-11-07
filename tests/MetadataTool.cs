using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace BareBonesDownloader
{
    internal static class MetadataTool
    {
        private static int Main(string[] args)
        {
            try
            {
                Console.OutputEncoding = new UTF8Encoding(false);
                if (args.Length == 3 && args[0] == "--install")
                {
                    DateTime created = File.GetCreationTimeUtc(args[2]);
                    DateTime modified = File.GetLastWriteTimeUtc(args[2]);
                    DateTime accessed = File.GetLastAccessTimeUtc(args[2]);
                    File.Replace(args[1], args[2], null);
                    File.SetCreationTimeUtc(args[2], created);
                    File.SetLastWriteTimeUtc(args[2], modified);
                    File.SetLastAccessTimeUtc(args[2], accessed);
                    Console.WriteLine("Installed verified tags without changing file dates.");
                    return 0;
                }
                if (args.Length != 3) throw new ArgumentException("Expected audio path, source info JSON, tools folder.");
                using (ProcessJob job = new ProcessJob())
                {
                    MusicMetadata.EnsureTags(args[0], args[1], args[2], job, delegate { return false; }, Console.WriteLine);
                    Console.WriteLine(new JavaScriptSerializer().Serialize(MusicMetadata.ReadTags(args[0], args[2], job, delegate { return false; })));
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }
    }
}
