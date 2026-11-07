using System;
using System.IO;
using System.Threading;
using System.Text;
using System.Web.Script.Serialization;

internal static class FakeDownloader
{
    private static string PathRecord(string value)
    {
        string json = new JavaScriptSerializer().Serialize(value);
        StringBuilder ascii = new StringBuilder();
        foreach (char c in json)
            if (c > 127) ascii.Append("\\u" + ((int)c).ToString("x4"));
            else ascii.Append(c);
        return "BBD_FILE:" + ascii.ToString();
    }

    private static int Main(string[] args)
    {
        Thread.Sleep(150); // Give the parent time to attach its Windows job.
        if (Path.GetFileNameWithoutExtension(Environment.GetCommandLineArgs()[0]) == "rsgain")
        {
            Console.WriteLine("Synthetic ReplayGain scan completed.");
            return 0;
        }
        if (Path.GetFileNameWithoutExtension(Environment.GetCommandLineArgs()[0]) == "ffmpeg")
        {
            int inputIndex = Array.IndexOf(args, "-i");
            if (inputIndex >= 0 && Path.GetFileName(args[inputIndex + 1]) == "Verification mismatch.opus")
            {
                File.Copy(args[inputIndex + 1], args[args.Length - 1], true);
                return 0;
            }
            Console.Error.WriteLine("Simulated metadata write failure.");
            return 9;
        }
        if (Path.GetFileNameWithoutExtension(Environment.GetCommandLineArgs()[0]) == "ffprobe")
        {
            string basename = Path.GetFileName(args[args.Length - 1]);
            if (basename == "Broken audio.opus")
            {
                Console.WriteLine("{\"streams\":[]}");
                return 0;
            }
            if (basename == "Tag warning.opus" || basename == "Verification mismatch.opus")
            {
                string title = Path.GetFileNameWithoutExtension(basename);
                Console.WriteLine("{\"streams\":[{\"codec_type\":\"audio\",\"tags\":{\"TITLE\":\"" + title + "\",\"ARTIST\":\"Test artist\",\"ALBUM\":\"Test album\",\"ALBUM_ARTIST\":\"Test artist\",\"DATE\":\"20260101\"}}]}");
                return 0;
            }
            if (Path.GetFileName(args[args.Length - 1]) == "Missing metadata.opus")
            {
                Console.WriteLine("{\"streams\":[{\"codec_type\":\"audio\",\"tags\":{}}]}");
                return 0;
            }
            Console.WriteLine("{\"streams\":[{\"codec_type\":\"audio\",\"tags\":{\"TITLE\":\"Regression (test)\",\"ARTIST\":\"Test artist\",\"ALBUM\":\"Test album\",\"ALBUM_ARTIST\":\"Test artist\",\"DATE\":\"20261031\"}}]}");
            return 0;
        }
        string folder = null;
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "--paths") folder = args[i + 1];
        string url = args[args.Length - 1];
        Console.Error.WriteLine("ERROR-like diagnostic with \"quotes\" <bad> | and a NUL:\0");
        Console.WriteLine("BBD_FILE:C:\\invalid<path.opus");
        if (url.EndsWith("AAAAAAAAAAA"))
        {
            Console.Error.WriteLine("ERROR: [WinError 32] The process cannot access the file because it is being used by another process: 'C:\\Music\\X.opus' -> 'C:\\Music\\X.opus'");
            return 1;
        }
        if (url.EndsWith("EEEEEEEEEEE") || url.EndsWith("FFFFFFFFFFF") || url.EndsWith("GGGGGGGGGGG"))
        {
            Directory.CreateDirectory(folder);
            string title = url.EndsWith("EEEEEEEEEEE") ? "Tag warning" : url.EndsWith("GGGGGGGGGGG") ? "Verification mismatch" : "Broken audio";
            string audio = Path.Combine(folder, title + ".opus");
            File.WriteAllText(audio, "synthetic audio");
            File.WriteAllText(Path.Combine(folder, title + ".info.json"),
                "{\"id\":\"" + url.Substring(url.Length - 11) + "\",\"title\":\"" + title + "\",\"artist\":\"Test artist\",\"album\":\"Test album\",\"release_date\":\"20261031\"}");
            Console.WriteLine(PathRecord(audio));
            return 0;
        }
        if (url.EndsWith("DDDDDDDDDDD"))
        {
            Directory.CreateDirectory(folder);
            string untagged = Path.Combine(folder, "Missing metadata.opus");
            File.WriteAllText(untagged, "untagged audio");
            Console.WriteLine(PathRecord(untagged));
            return 0;
        }
        Directory.CreateDirectory(folder);
        string output = Path.Combine(folder, "Regression (test).opus");
        File.WriteAllText(Path.Combine(folder, "Regression (test).webm"), "intermediate");
        if (url.EndsWith("CCCCCCCCCCC"))
        {
            Thread.Sleep(30000);
            return 1;
        }
        File.WriteAllText(Path.Combine(folder, "Regression (test).info.json"),
            "{\"id\":\"" + url.Substring(url.Length - 11) + "\",\"title\":\"Regression (test)\",\"artist\":\"Test artist\",\"album\":\"Test album\",\"release_date\":\"20261031\"}");
        File.WriteAllText(output, "finished synthetic audio");
        Console.WriteLine(PathRecord(output));
        return 0;
    }
}
