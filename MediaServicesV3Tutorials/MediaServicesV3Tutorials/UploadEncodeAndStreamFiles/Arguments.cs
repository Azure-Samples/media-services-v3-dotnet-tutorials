using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EncodeAndStreamFiles
{
    public class Arguments
    {
        public string OutputFolder { get; private set; }
        public string InputUrl { get; private set; }
        public string InputFile { get; private set; }
        public string NamePrefix { get; private set; }
        public string InputExtension { get; private set; }
        public static Arguments ParseArguments(string[] args)
        {
            Arguments arguments = new Arguments();

            arguments.NamePrefix = string.Empty;

            for (int i = 0; i < args.Length; i += 2)
            {
                if (String.Compare(args[i], "-inputUrl", StringComparison.OrdinalIgnoreCase) == 0)
                {
                    arguments.InputUrl = args[i + 1];
                    Uri inputUri = null;
                    if (!Uri.TryCreate(arguments.InputUrl, UriKind.Absolute, out inputUri))
                    {
                        arguments.InputExtension = string.Empty;
                    }
                    else
                    {
                        arguments.InputExtension = Path.GetExtension(inputUri.LocalPath);
                    }
                }
                else if (String.Compare(args[i], "-inputFile", StringComparison.OrdinalIgnoreCase) == 0)
                {
                    arguments.InputFile = args[i + 1];
                    arguments.InputExtension = Path.GetExtension(arguments.InputFile);
                }
                else if (String.Compare(args[i], "-namePrefix", StringComparison.OrdinalIgnoreCase) == 0)
                {
                    arguments.NamePrefix = args[i + 1];
                }
                else if (String.Compare(args[i], "-outputFolder", StringComparison.OrdinalIgnoreCase) == 0)
                {
                    arguments.OutputFolder = args[i + 1];
                }
                else
                {
                    Console.WriteLine($"Unknown parameter {args[i]}");
                }
            }

            if (arguments.Verify())
            {
                return arguments;
            }
            else
            {
                return null;
            }
        }

        private void PrintUsage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine();
            Console.WriteLine("\tVideoIndexerSample.exe -inputUrl inputUrl -namePrefix namePrefix -outputFolder outputFolder");
            Console.WriteLine("\t\tOR");
            Console.WriteLine("\tVideoIndexerSample.exe -inputFile fullPathToLocalFile -namePrefix namePrefix -outputFolder outputFolder");
        }

        private bool Verify()
        {
            if (string.IsNullOrEmpty(OutputFolder))
            {
                Console.WriteLine("An output folder was not specifed and it is required.");
                Console.WriteLine();
                PrintUsage();
                return false;
            }

            if (!Directory.Exists(OutputFolder))
            {
                Console.WriteLine($"OutputFolder {OutputFolder} does not exist.");
                return false;
            }

            if (!string.IsNullOrEmpty(InputFile) && !string.IsNullOrEmpty(InputUrl))
            {
                Console.WriteLine("An InputFile or an InputUrl must be specified.");
                Console.WriteLine();
                PrintUsage();
                return false;
            }

            if (string.IsNullOrEmpty(InputFile) && string.IsNullOrEmpty(InputUrl))
            {
                Console.WriteLine("Either an InputFile or an InputUrl must be specified but not both.");
                Console.WriteLine();
                PrintUsage();
                return false;
            }

            if (!string.IsNullOrEmpty(InputFile) && !File.Exists(InputFile))
            {
                Console.WriteLine($"InputFile {InputFile} does not exist.");
                return false;
            }

            return true;
        }
    }
}
