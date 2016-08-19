﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Bivrost.Log
{

	public interface LogListener
	{
		void Write(string time, LogType type, string msg, string path);
	}

	#region writers
	/// <summary>
	/// Windows Event Log Writer.
	/// </summary>
	public class WindowsEventLogListener : LogListener
	{

		string sSource;

		static EventLogEntryType ToEventLogEntryType(LogType t)
		{
			switch (t)
			{
				default:
				case LogType.info:
				case LogType.notification:
					return EventLogEntryType.Information;
				case LogType.error:
					return EventLogEntryType.Warning;
				case LogType.fatal:
					return EventLogEntryType.Error;
			}
		}


		/// <summary>
		/// Logger with custom application name. This requires admin rights.
		/// </summary>
		/// <param name="appname"></param>
		/// <param name="logname"></param>
		public WindowsEventLogListener(string appname, string logname)
		{
			if (!EventLog.SourceExists(sSource))
				EventLog.CreateEventSource(sSource, logname);
			sSource = appname;
		}


		/// <summary>
		/// Logger with generic "Application" application name.
		/// </summary>
		public WindowsEventLogListener()
		{
			sSource = "Application";
		}


		public void Write(string time, LogType type, string msg, string path)
		{
			EventLog.WriteEntry(sSource, type + ": " + msg, ToEventLogEntryType(type));
		}

	}


	/// <summary>
	/// Writing to the console
	/// </summary>
	public class TraceLogListener : LogListener
	{
		public void Write(string time, LogType type, string msg, string path)
		{
			Trace.WriteLine(time, type.ToString());
			Trace.Indent();
			Trace.WriteLine(msg);
			Trace.WriteLine("");
			Trace.WriteLine("at " + path);
			Trace.Unindent();
			Trace.Flush();
		}
	}


	/// <summary>
	/// Writing to a text file
	/// </summary>
	public class TextFileLogListener : LogListener
	{

		public string LogFile { get; protected set; }


		public TextFileLogListener(string logDirectory, string logPrefix = "log", string version = null)
		{
			string now = DateTime.Now.ToString("yyyy-MM-ddTHHmmss");
			if(version == null)
#if DEBUG
				version = "DEBUG";
#else
				version = "v" + Assembly.GetEntryAssembly().GetName().Version.ToString();
#endif

			LogFile = logDirectory + string.Format("{2}-{0}-{1}.txt", version, now, logPrefix);

			Logger.Info("Log file: " + LogFile);
		}


		public void Write(string time, LogType type, string msg, string path)
		{
			lock (LogFile)
			{
				try
				{
					File.AppendAllText(
						LogFile,
						string.Format(
							"[{0}] {1}\r\n\t{2}\r\n\r\nat {3}\r\n",
							type,
							time,
							msg.Trim().Replace("\r\n", "\r\n\t"),
							path
						)
					);
				}
				catch (Exception e)
				{
					Console.Error.WriteLine("Error writing to text log: " + e);
				}
			}
		}
	}

	#endregion

	public enum LogType
	{
		info,
		error,
		notification,
		fatal
	}


	public static class Logger
	{

		/// <summary>
		/// Returns a normalized path relative to the logger (or provided sourceFilePath)
		/// </summary>
		/// <param name="path"></param>
		/// <param name="sourceFilePath"></param>
		/// <returns></returns>
		static string NormalizePath(string path, [CallerFilePath] string sourceFilePath = "")
		{
			int l = Math.Min(path.Length, sourceFilePath.Length);
			for (int i = 0; i < l; i++)
				if (path[i] != sourceFilePath[i])
					return path.Substring(i);
			return "(Logger)"; // normalization failed
		}


		static string PathUtil(string sourceFilePath, int sourceLineNumber, string memberName)
		{
			return string.Format("{0}#{1} ({2})", NormalizePath(sourceFilePath), sourceLineNumber, memberName);
		}


		struct LogElement { public string now; public LogType type; public string msg; public string path; }
		static ConcurrentQueue<LogElement> logElementQueue = new ConcurrentQueue<LogElement>();

		static void WriteLogEntry(LogType type, string msg, string path)
		{
			string now = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");

			// normalize newlines to windows format
			msg = msg.Replace("\r\n", "\n").Replace("\n", "\r\n");

			logElementQueue.Enqueue(new LogElement() { now = now, type = type, msg = msg, path = path });
		}


		static void WriteLogThread()
		{
			LogElement e;
			while (true)
			{
				if (logElementQueue.TryDequeue(out e))
					foreach (var l in listeners)
						l.Write(e.now, e.type, e.msg, e.path);
			}
		}


		static HashSet<LogListener> listeners = new HashSet<LogListener>();
		

		public static LogListener[] LogListeners
		{
			get {
				var r = new LogListener[listeners.Count];
				listeners.CopyTo(r);
				return r;
			}
		}


		static Thread thread;


		public static void RegisterListener(LogListener lw)
		{
			lock(listeners)
			{
				if (thread == null)
				{
					thread = new Thread(new ThreadStart(WriteLogThread))
					{
						IsBackground = true,
						Name = "log listener thread"
					};
					thread.Start();
				}
			}
			listeners.Add(lw);
			Info("Registered log writer: " + lw);
		}


		public static void UnregisterListener(LogListener lw)
		{
			listeners.Remove(lw);
			Info("Unregistered log writer: " + lw);
		}


		/// <summary>
		/// Use for registering degug information.
		/// Not displayed on screen.
		/// </summary>
		/// <param name="msg">the message</param>
		/// <param name="memberName">(automatically added) source code trace information</param>
		/// <param name="sourceFilePath">(automatically added) source code trace information</param>
		/// <param name="sourceLineNumber">(automatically added) source code trace information</param>
		public static void Info(
			string msg, 
			[CallerMemberName] string memberName = "",
			[CallerFilePath] string sourceFilePath = "",
			[CallerLineNumber] int sourceLineNumber = 0
		)
		{
			WriteLogEntry(LogType.info, msg, PathUtil(sourceFilePath, sourceLineNumber, memberName));
		}


		/// <summary>
		/// Use for registering non fatal errors.
		/// Not displayed on screen.
		/// </summary>
		/// <param name="msg">the message</param>
		/// <param name="memberName">(automatically added) source code trace information</param>
		/// <param name="sourceFilePath">(automatically added) source code trace information</param>
		/// <param name="sourceLineNumber">(automatically added) source code trace information</param>
		public static void Error(
			string msg,
			[CallerMemberName] string memberName = "",
			[CallerFilePath] string sourceFilePath = "",
			[CallerLineNumber] int sourceLineNumber = 0
		)
		{
			WriteLogEntry(LogType.error, msg, PathUtil(sourceFilePath, sourceLineNumber, memberName));
		}


		/// <summary>
		/// Use for registering non fatal errors in form of exceptions.
		/// Not displayed on screen.
		/// </summary>
		/// <param name="e">The exception that signalled the error</param>
		/// <param name="additionalMsg">an optional message</param>
		/// <param name="memberName">(automatically added) source code trace information</param>
		/// <param name="sourceFilePath">(automatically added) source code trace information</param>
		/// <param name="sourceLineNumber">(automatically added) source code trace information</param>
		public static void Error(
			Exception e, 
			string additionalMsg="Exception", 
			[CallerMemberName] string memberName = "",
			[CallerFilePath] string sourceFilePath = "",
			[CallerLineNumber] int sourceLineNumber = 0
		)
		{
			Error(additionalMsg + "\n" + e, memberName, sourceFilePath, sourceLineNumber);
		}


		/// <summary>
		/// Use for displaying fatal errors. 
		/// The application is supposed to quit soon after this is called.
		/// This message will be displayed on screen.
		/// </summary>
		/// <param name="msg">the message</param>
		/// <param name="memberName">(automatically added) source code trace information</param>
		/// <param name="sourceFilePath">(automatically added) source code trace information</param>
		/// <param name="sourceLineNumber">(automatically added) source code trace information</param>
		public static void Fatal(
			string msg,
			[CallerMemberName] string memberName = "",
			[CallerFilePath] string sourceFilePath = "",
			[CallerLineNumber] int sourceLineNumber = 0
		)
		{
			WriteLogEntry(LogType.error, msg, PathUtil(sourceFilePath, sourceLineNumber, memberName));
			System.Windows.MessageBox.Show(msg, "Fatal error");
		}


		/// <summary>
		/// Use for displaying fatal errors in form of exceptions. 
		/// The application is supposed to quit soon after this is called.
		/// This message will be displayed on screen.
		/// </summary>
		/// <param name="e">The exception that signalled the error</param>
		/// <param name="additionalMsg">an additional message</param>
		/// <param name="memberName">(automatically added) source code trace information</param>
		/// <param name="sourceFilePath">(automatically added) source code trace information</param>
		/// <param name="sourceLineNumber">(automatically added) source code trace information</param>
		public static void Fatal(
			Exception e,
			string additionalMsg = "",
			[CallerMemberName] string memberName = "",
			[CallerFilePath] string sourceFilePath = "",
			[CallerLineNumber] int sourceLineNumber = 0
		)
		{
			Fatal((additionalMsg + "\n").Trim() + e, memberName, sourceFilePath, sourceLineNumber);
		}


		static Logger()
		{
			RegisterListener(new TraceLogListener());
		}

	}
}