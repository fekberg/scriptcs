using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography.X509Certificates;
using Common.Logging;
using ScriptCs.Contracts;

namespace ScriptCs
{
    public class Repl : ScriptExecutor
    {
        private readonly string[] _scriptArgs;

        private readonly IObjectSerializer _serializer;

        private readonly IDictionary<string, Func<ScriptResult>> _commands;

        public Repl(
            string[] scriptArgs,
            IFileSystem fileSystem,
            IScriptEngine scriptEngine,
            IObjectSerializer serializer,
            ILog logger,
            IConsole console,
            IFilePreProcessor filePreProcessor) : base(fileSystem, filePreProcessor, scriptEngine, logger)
        {
            _scriptArgs = scriptArgs;
            _serializer = serializer;
            Console = console;

            _commands = new Dictionary<string, Func<ScriptResult>>
            {
                { "clear", () => { Console.Clear(); return new ScriptResult(); }},
                { "reset", () => { Reset(); return new ScriptResult(); }},
                { "exit", () => { Terminate(); return null; }}
            };
        }

        public string Buffer { get; set; }

        public IConsole Console { get; private set; }

        public override void Terminate()
        {
            base.Terminate();
            Logger.Debug("Exiting console");
            Console.Exit();
        }

        public override ScriptResult Execute(string script, params string[] scriptArgs)
        {
            Guard.AgainstNullArgument("script", script);

            try
            {
                if (script.StartsWith("#", StringComparison.OrdinalIgnoreCase))
                {
                    var command = new string(script.Skip(1).TakeWhile(c => c != ' ').ToArray());
                    if (!string.IsNullOrEmpty(command) && _commands.ContainsKey(command))
                        return _commands[command]();
                }

                var preProcessResult = FilePreProcessor.ProcessScript(script);

                ImportNamespaces(preProcessResult.Namespaces.ToArray());

                foreach (var reference in preProcessResult.References)
                {
                    var referencePath = FileSystem.GetFullPath(Path.Combine(Constants.BinFolder, reference));
                    AddReferences(FileSystem.FileExists(referencePath) ? referencePath : reference);
                }

                Console.ForegroundColor = ConsoleColor.Cyan;

                Buffer += preProcessResult.Code;

                var result = ScriptEngine.Execute(Buffer, _scriptArgs, References, DefaultNamespaces, ScriptPackSession);
                if (result == null) return new ScriptResult();

                if (result.CompileExceptionInfo != null)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine(result.CompileExceptionInfo.SourceException.Message);
                }

                if (result.ExecuteExceptionInfo != null)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine(result.ExecuteExceptionInfo.SourceException.Message);
                }

                if (result.IsPendingClosingChar)
                {
                    return result;
                }

                if (result.ReturnValue != null)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;

                    var serializedResult = _serializer.Serialize(result.ReturnValue);

                    Console.WriteLine(serializedResult);
                }

                Buffer = null;
                return result;
            }
            catch (FileNotFoundException fileEx)
            {
                RemoveReferences(fileEx.FileName);
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\r\n" + fileEx + "\r\n");
                return new ScriptResult { CompileExceptionInfo = ExceptionDispatchInfo.Capture(fileEx) };
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\r\n" + ex + "\r\n");
                return new ScriptResult { ExecuteExceptionInfo = ExceptionDispatchInfo.Capture(ex) };
            }
            finally
            {
                Console.ResetColor();
            }
        }
    }
}
