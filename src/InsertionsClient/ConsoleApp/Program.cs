﻿// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Net.Insertions.Api;
using Microsoft.Net.Insertions.Api.Providers;
using Microsoft.Net.Insertions.Common.Constants;
using Microsoft.Net.Insertions.Common.Logging;
using Microsoft.Net.Insertions.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Microsoft.Net.Insertions.ConsoleApp
{
	internal class Program
	{
		private const string SwitchDefaultConfig = "-d:";

		private const string SwitchManifest = "-m:";

		private const string SwitchIgnorePackages = "-i:";

		private const string SwitchIgnoreDevUxTeamPackages = "-idut";

		private const string SwitchPropsFilesRootDir = "-p:";

		private const string SwitchFeedAccessToken = "-a:";

		private const string SwitchMaxWaitSeconds = "-w:";

		private const string SwitchMaxConcurrency = "-c:";

		private static readonly Lazy<string> HelpParameters = new Lazy<string>(() =>
		{
			StringBuilder txt = new StringBuilder();

			txt.Append($"{SwitchDefaultConfig}<default.config full file path>");
			txt.Append(" ");
			txt.Append($"{SwitchManifest}<manifest.json full file path>");
			txt.Append(" ");
			txt.Append($"{SwitchIgnorePackages}<ignored packages file path>");
			txt.Append(" ");
			txt.Append($"{SwitchPropsFilesRootDir}<root directory that contains props files>");
			txt.Append(" ");
			txt.Append($"{SwitchFeedAccessToken}<token to access nuget feed>");
			txt.Append(" ");
			txt.Append($"{SwitchMaxWaitSeconds}<maximum seconds to allow job run, as int>");
			txt.Append(" ");
			txt.Append($"{SwitchMaxConcurrency}<max concurrent default.config updates, as int>");

			return txt.ToString();
		});

		private static readonly Lazy<string> ProgramName = new Lazy<string>(() => Assembly.GetExecutingAssembly().GetName().Name!);

		private static string DefaultConfigFile = string.Empty;

		private static string ManifestFile = string.Empty;

		private static string IgnoredPackagesFile = string.Empty;

		private static bool IgnoreDevUxTeamPackagesScenario;

		private static string PropsFilesRootDirectory = string.Empty;

		private static string FeedAccessToken = string.Empty;

		private static int MaxWaitSeconds = 75;

		private static int MaxConcurrency = 4;


		/// <summary>
		/// Sets up logging based on the TRACE switch being set.
		/// </summary>
		static Program()
		{
			string logDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Logs");
			if (!Directory.Exists(logDirectory))
			{
				Directory.CreateDirectory(logDirectory);
			}

			LogFile = Path.Combine(logDirectory, $"log_{DateTime.Now.Ticks}.txt");

			Trace.AutoFlush = true;
			_ = Trace.Listeners.Add(new InsertionsTextWriterTraceListener(LogFile, "tracelistener"));
			_ = Trace.Listeners.Add(new InsertionsConsoleTraceListener());
		}


		private static string LogFile { get; }

		[STAThread]
		private static void Main(string[] args)
		{
			ShowStartOrEndMessage($"Running {ProgramName.Value}");

			ProcessCmdArguments(args);

			IInsertionApiFactory apiFactory = new InsertionApiFactory();
			IInsertionApi api = apiFactory.Create(MaxWaitSeconds, MaxConcurrency);

			UpdateResults results;
			if (!string.IsNullOrWhiteSpace(IgnoredPackagesFile))
			{
				results = api.UpdateVersions(ManifestFile, DefaultConfigFile, IgnoredPackagesFile, FeedAccessToken, PropsFilesRootDirectory);
			}
			else if (IgnoreDevUxTeamPackagesScenario)
			{
				results = api.UpdateVersions(ManifestFile, DefaultConfigFile, InsertionConstants.DefaultDevUxTeamPackages, FeedAccessToken, PropsFilesRootDirectory);
			}
			else
			{
				results = api.UpdateVersions(ManifestFile, DefaultConfigFile, (HashSet<string>?)null, FeedAccessToken, PropsFilesRootDirectory);
			}

			ShowResults(results);

			Trace.WriteLine($"Log: {LogFile}{Environment.NewLine}");
		}

		private static void ShowResults(UpdateResults results)
		{
			Console.ForegroundColor = results.Outcome ? ConsoleColor.Green : ConsoleColor.Red;
			Console.WriteLine($"Completed {(results.Outcome ? "successfully" : "in a failure")}.");
			if (!results.Outcome)
			{
				Console.WriteLine($"Details: {results.OutcomeDetails}.");
			}
			Console.ResetColor();
			Trace.WriteLine($"Duration: {results.DurationMilliseconds:N2}-ms.");
			Trace.WriteLine($"Successful updates: {results.UpdatedNuGets.Count():N0}.");
			Trace.WriteLine("Updated default.config NuGet package versions...");
			foreach (PackageUpdateResult updatedNuget in results.UpdatedNuGets.OrderBy(r => r.PackageId))
			{
				Trace.WriteLine($"           {updatedNuget.PackageId}: {updatedNuget.NewVersion}");
			}

			if (results.PropsFileUpdateResults != null)
			{
				Trace.WriteLine($"Updated {results.PropsFileUpdateResults.UpdatedVariables.Count} .props files:");
				foreach (var propsFile in results.PropsFileUpdateResults.UpdatedVariables.Where(r => r.Value.Count != 0).OrderBy(p => p.Key.Path))
				{
					Trace.WriteLine($"        {propsFile.Key.Path}");
					foreach (var variableChange in propsFile.Value)
					{
						Trace.WriteLine($"                {variableChange.Name}={variableChange.Value}");
					}
				}

				if(results.PropsFileUpdateResults.UnrecognizedVariables.Count != 0)
				{
					Trace.WriteLine($"{results.PropsFileUpdateResults.UnrecognizedVariables.Count} variables were not found in props files:");
					foreach (var variable in results.PropsFileUpdateResults.UnrecognizedVariables)
					{
						Trace.WriteLine($"        {variable.Name} in {variable.ReferencedFilePath}");
					}
				}
			}
		}

		private static void ShowStartOrEndMessage(string message)
		{
			Console.ForegroundColor = ConsoleColor.Green;
			Console.WriteLine(message);
			Console.ResetColor();
		}

		private static void ShowHelp()
		{
			Console.WriteLine($"{Environment.NewLine}");
			Console.WriteLine($"{ProgramName.Value} updates versions of NuGet packages in {InsertionConstants.DefaultConfigFile} with the corresponding values from {InsertionConstants.ManifestFile}{Environment.NewLine}");

			Console.WriteLine($"Usage:");
			Console.WriteLine($">{ProgramName.Value}.exe {HelpParameters.Value}");

			Console.WriteLine($"{Environment.NewLine}Options:");
			Console.WriteLine($"{SwitchDefaultConfig}   full path on disk to default.config to update");
			Console.WriteLine($"{SwitchManifest}   full path on disk to manifest.json");
			Console.WriteLine($"{SwitchIgnorePackages}   full path on disk to ignored packages file. Each line should have a package id [optional]");
			Console.WriteLine($"{SwitchPropsFilesRootDir}   directory to search for and update .props files [optional]");
			Console.WriteLine($"{FeedAccessToken}   token to access nuget feed. Necessary when updating props files [optional]");
			Console.WriteLine($"{SwitchMaxWaitSeconds}   maximum allowed duration in seconds [optional]");
			Console.WriteLine($"{SwitchMaxConcurrency}   maximum concurrency of default.config version updates [optional]{Environment.NewLine}");

			Console.ForegroundColor = ConsoleColor.Green;
			Console.WriteLine("Example...");
			Console.WriteLine($">{ProgramName.Value} {SwitchDefaultConfig}c:\\default.config {SwitchManifest}c:\\manifest.json");
			Console.ResetColor();
		}

		private static void ShowErrorHelpAndExit(string reason)
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine($"Exiting due to incorrect input.  Reason: {reason}");
			Console.ResetColor();
			ShowHelp();
			Environment.Exit(1);
		}

		private static void ProcessCmdArguments(string[] args)
		{
			Console.ForegroundColor = ConsoleColor.DarkYellow;
			Console.WriteLine("Processing CMD line parameters");
			Console.ResetColor();

			if (args == null || args.Length < 2)
			{
				ShowErrorHelpAndExit("incorrect # of parameters specified");
			}

			foreach (string arg in args!)
			{
				if (arg.StartsWith(SwitchDefaultConfig))
				{
					ProcessArgument(arg, SwitchDefaultConfig, $"Specified {InsertionConstants.DefaultConfigFile}:", ref DefaultConfigFile);
				}
				else if (arg.StartsWith(SwitchManifest))
				{
					ProcessArgument(arg, SwitchManifest, $"Specified {InsertionConstants.ManifestFile}:", ref ManifestFile);
				}
				else if (arg.StartsWith(SwitchIgnorePackages))
				{
					ProcessArgument(arg, SwitchIgnorePackages, $"Specified ignored packages file:", ref IgnoredPackagesFile);
				}
				else if (arg.StartsWith(SwitchPropsFilesRootDir))
				{
					ProcessArgument(arg, SwitchPropsFilesRootDir, $"Specified root directory for props files:", ref PropsFilesRootDirectory);
				}
				else if (arg.StartsWith(SwitchFeedAccessToken))
				{
					FeedAccessToken = arg.Replace(SwitchFeedAccessToken, string.Empty);
					Trace.WriteLine($"CMD line param. An access token was specified.");
				}
				else if (arg.StartsWith(SwitchMaxWaitSeconds))
				{
					ProcessArgumentInt(arg, SwitchMaxWaitSeconds, $"Specified \"max wait seconds\":", ref MaxWaitSeconds);
				}
				else if (arg.StartsWith(SwitchMaxConcurrency))
				{
					ProcessArgumentInt(arg, SwitchMaxConcurrency, $"Specified \"max concurrency\":", ref MaxConcurrency);
				}
				else if (arg.StartsWith(SwitchIgnoreDevUxTeamPackages))
				{
					IgnoreDevUxTeamPackagesScenario = true;
				}
			}

			if (string.IsNullOrWhiteSpace(DefaultConfigFile))
			{
				ShowErrorHelpAndExit($"{InsertionConstants.DefaultConfigFile} path not set.");
			}
			if (string.IsNullOrWhiteSpace(ManifestFile))
			{
				ShowErrorHelpAndExit($"{InsertionConstants.ManifestFile} path not set.");
			}
		}

		private static void ProcessArgument(string argument, string appSwitch, string cmdLineMessage, ref string target)
		{
			target = argument.Replace(appSwitch, string.Empty);
			Trace.WriteLine($"CMD line param. {cmdLineMessage} {target}");
		}

		private static void ProcessArgumentInt(string argument, string appSwitch, string cmdLineMessage, ref int target)
		{
			string trimmedArg = argument.Replace(appSwitch, string.Empty);
			if (int.TryParse(trimmedArg, out target))
			{
				Trace.WriteLine($"CMD line param. {cmdLineMessage} {target}");
			}
			else
			{
				target = -1;
				Trace.WriteLine("Specified value is not an integer. Default value will be used.");
			}
		}
	}
}