using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Collections;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ProcessOutputJson
{
    public static class ProcessJsonProgram
    {
        static string _jsonfile = @"c:\SupportFiles\OCR\Output\presentation_videoocr.json";
        static string _videofile = @"c:\SupportFiles\OCR\presentation.mp4";

        static Regex _regExPSN = new Regex("PSN",
            RegexOptions.Compiled | RegexOptions.Multiline);
        static Regex _regExCODE = new Regex(
                        "([\\w\\d]{4}\\S?\\-\\S?){2}[\\w\\d]{4}",
            RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly string _exeName = Path.GetFileName(Assembly.GetExecutingAssembly().Location);
        public static void Main(string[] args)
        {
            bool needJpegs = true;
            bool needVideo = false;

            PrintScreenOfBgColor(ConsoleColor.Gray);
            Console.ForegroundColor = ConsoleColor.Black;
            //if (args.Length == 0 || args.All(string.IsNullOrWhiteSpace))
            //    PrintArgsAndSyntaxAndAwaitEnterKeyThenQuit(args);

            if (args.Length != 0 && (args[0][0] != '/'))
            {
                if (args.Length == 1)
                    PrintArgsAndSyntaxAndAwaitEnterKeyThenQuit(args);
                if (args[1][0] != '/')
                    PrintArgsAndSyntaxAndAwaitEnterKeyThenQuit(args);
                _jsonfile = args[0];
                _videofile = args[1];

            }

            if (args.Any(x => x.ToLowerInvariant() == "/v")) needVideo = true;
            if (needVideo && args.All(x => x.ToLowerInvariant() != "/j"))
                needJpegs = false;

            if (!(File.Exists(_jsonfile) && File.Exists(_videofile)))
                PrintArgsAndSyntaxAndAwaitEnterKeyThenQuit(args);

            var matches= ExtractRegExMatches();

            ProcessOutputs(needJpegs, needVideo, matches);


            Console.ForegroundColor = ConsoleColor.Black;
            Console.ReadLine();

            //Input the file and regex match according to args.
            //JArray jsonVal = JArray.Load(
            //    new JsonTextReader(
            //        TextReader.Synchronized(
            //            new StreamReader(_jsonfile)
            //            )
            //        )
            //    ) as JArray;

        }

        private static void ProcessOutputs(bool needJpegs, bool needVideo, List<JToken> matches)
        {
            CreateConfigsForMatches();
            QueueBatchOfTranscodingJobs();
            AwaitProgress();
        }

        private static void AwaitProgress()
        {
            throw new NotImplementedException();
        }

        private static void QueueBatchOfTranscodingJobs()
        {
            throw new NotImplementedException();
        }

        private static void CreateConfigsForMatches()
        {
            throw new NotImplementedException();
        }

        private static List<JToken> ExtractRegExMatches()
        {
            var matches = new List<JToken>();
            var fileStream = new FileStream(_jsonfile, FileMode.Open);
            var streamJson = new StreamReader(fileStream);
            var strJson = streamJson.ReadToEnd();
            var isMatch = _regExPSN.IsMatch(strJson);
            if (isMatch)
            {
                dynamic jsonVal = JValue.Parse(strJson);


                foreach (dynamic item in jsonVal.fragments)
                {
                    if (item.events == null) continue;
                    foreach (dynamic evtArray in item.events)
                    {
                        if (evtArray == null) continue;
                        var i = 0;
                        foreach (dynamic evt in evtArray)
                        {
                            var starttime = item.start + (item.interval * i);
                            var endtime = starttime + (item.interval * (i + 1));
                            if (evt.region == null) continue;
                            if (evt.region.lines == null) continue;

                            foreach (dynamic lineOfText in evt.region.lines)
                            {
                                if (lineOfText.text != null)
                                {
                                    if (_regExCODE.IsMatch(lineOfText.text.ToString()))
                                    {
                                        lineOfText.start = starttime;
                                        lineOfText.end = endtime;
                                        matches.Add(lineOfText);
                                        

                                        Console.ForegroundColor = ConsoleColor.DarkBlue;
                                        Console.WriteLine(
                                            $"Start: {starttime} End: {endtime}  " +
                                            JsonConvert.SerializeObject(lineOfText));
                                        Console.Beep(620, 30);


                                        //  "version": 1,
                                        // "timescale": 90000,
                                        // "offset": 0,
                                        // "framerate": 60,
                                        // "width": 1920,
                                        // "height": 1080,
                                        // "totalDuration": 39469500,
                                        // "fragments": [
                                        // {
                                        //   "start": 0,
                                        //   "duration": 270000,
                                        //   "interval": 135000,
                                        //   "events": [
                                        //    [
                                        //      {
                                        //          "region": {
                                        //              "language": "English",
                                        //              "orientation": "Up",
                                        //              "lines": [
                                        //              {
                                        //                  "text":
                                    }

                                }
                            }
                            i++;
                        }
                    }
                }

            }

            return matches;
        }

        private static void PrintArgsAndSyntaxAndAwaitEnterKeyThenQuit(string[] args, ConsoleColor bgcolor = ConsoleColor.White)
        {
            PrintScreenOfBgColor(bgcolor);
            Console.BackgroundColor = bgcolor;
            Console.ForegroundColor = ConsoleColor.Black;
            Console.WriteLine("Takes an Azure OCR Output json file, matches text against a regex,");
            Console.WriteLine("and outputs video snippets or jpegs according to the matches.");
            Console.WriteLine();
            Console.WriteLine("Syntax:");
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine($"{_exeName} <OCR output json file> <input video> /j /v");
            Console.ForegroundColor = ConsoleColor.Black;
            Console.WriteLine();
            Console.WriteLine("Where all parameters are optional, however if specifying the json,"
                              + "\n one must also specify the input video and vice-versa.\n" +
                " /j = jpeg and /v = video, both are optional and request output accordingly.");
            Console.WriteLine();
            Console.WriteLine($"Default for json file: {_jsonfile}");
            Console.WriteLine($"Default for video file: {_videofile}");
            Console.WriteLine($"Default for output format is jpeg.");
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("You supplied:");
            Console.WriteLine($"{_exeName} {String.Join(" ", args)}");
            //Console.WriteLine($"{String.Join(" ", Environment.GetCommandLineArgs())}" );
            Console.WriteLine("\r\n");
            Console.ForegroundColor = ConsoleColor.Black;
            Console.WriteLine("\r\n");
            Console.Beep(440, 120);
            Console.WriteLine("Press enter to exit.");
            Console.WriteLine("\n");
            Console.ReadLine();

            Environment.Exit(160);
            //https://msdn.microsoft.com/en-us/library/windows/desktop/ms681382(v=vs.85).aspx
            //ERROR_INVALID_PARAMETER
            //87(0x57)
            //The parameter is incorrect.
            //
            //ERROR_BAD_ARGUMENTS
            //160(0xA0)
            //One or more arguments are not correct.
        }

        private static void PrintScreenOfBgColor(ConsoleColor bgColor)
        {
            Console.BackgroundColor = bgColor;
            var s = "";
            for (int i = 0; i < Console.WindowWidth; i++)
            {
                s += " ";
            }

            for (int i = 0; i < Console.WindowHeight + 1; i++)
            {
                Console.Write(s);
            }
            Console.SetCursorPosition(0, 0);
        }
    }
}
