using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LightLog
{
    public class LogManager
    {
        /// <summary>
        /// 日志文件所在的目录
        /// 默认为当前组件所在的文件夹
        /// </summary>
        public static string LogDirectory { set; get; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory + "Logs");
        /// <summary>
        /// 日志文件名
        /// 默认为Log.txt
        /// </summary>
        public static string LogFile { set; get; } = "Log.txt";

        /// <summary>
        /// 批量写入文件的日志数量
        /// 达到BatchCount即写入一次，如果未达到数量则等待BatchTimeout后写入一次
        /// 默认为1000
        /// </summary>
        public static int BatchCount { set; get; } = 1000;
        /// <summary>
        /// 当前已进入队列的日志数量
        /// </summary>
        private static int _currentLogCount;

        /// <summary>
        /// 批量写入文件的等待时间
        /// 日志数量达到BatchCount即写入一次，如果未达到数量则等待BatchTimeout后写入一次
        /// 默认为10毫秒
        /// </summary>
        public static int BatchTimeout { set; get; } = 10;

        /// <summary>
        /// 是否显示调试信息
        /// 默认为false
        /// </summary>
        public static bool ShowDebugInfo { set; get; } = false;

        /// <summary>
        /// 读写锁
        /// </summary>
        private static readonly ReaderWriterLockSlim LogWriteLock = new ReaderWriterLockSlim();

        /// <summary>
        /// 记录日志的后台任务，类初始化或调用Start之后启动，调用Stop之后停止
        /// </summary>
        private static Task _task;

        /// <summary>
        /// 是否停止记录日志
        /// 默认为false
        /// </summary>
        private static bool _stop = false;

        /// <summary>
        /// 日志同步队列
        /// 记录日志时先写入队列中再定时写入日志文件
        /// </summary>
        private static readonly BlockingCollection<string> LogBlockingCollection = new BlockingCollection<string>();

        /// <summary>
        /// 日志信息暂存
        /// </summary>
        private static readonly StringBuilder SbLog = new StringBuilder();

        private const string LIGHTLOG_DEBUG_PREFIX = "LightLog: ";

        private const string LOG_TIME_FORMAT = "yyyy-MM-dd HH:mm:ss:fff";

        private const string DEBUG_TIME_FORMAT = "yyyy-MM-dd HH:mm:ss:fffffff";

        static LogManager()
        {
            Directory.CreateDirectory(LogDirectory);

            Start();
        }

        private static void LogTask()
        {
            WriteDebugInfo("Task Started!");
            while (!_stop)
            {
                if (LogBlockingCollection.TryTake(out string item, BatchTimeout))
                {
                    if (ShowDebugInfo)
                    {
                        WriteDebugInfo($"TakeTime: {DateTime.Now.ToString(DEBUG_TIME_FORMAT)}");
                    }

                    SbLog.Append($"{DateTime.Now.ToString(LOG_TIME_FORMAT)}{Environment.NewLine}{item}{Environment.NewLine}{Environment.NewLine}");

                    if (Interlocked.Increment(ref _currentLogCount) >= BatchCount)
                    {
                        DoWriteLog();
                        WriteDebugInfo($"BatchWrite: {DateTime.Now.ToString(DEBUG_TIME_FORMAT)}");
                    }
                }
                else
                {
                    if (SbLog.Length > 0)
                    {
                        DoWriteLog();
                        WriteDebugInfo($"TimeoutWrite: {DateTime.Now.ToString(DEBUG_TIME_FORMAT)}");
                    }
                }
            }
            WriteDebugInfo("Task Stopped!");
        }

        public static void Start()
        {
            _stop = false;
            if (_task == null
                || _task.IsCompleted)
            {
                // Task可以不释放，可参考这里：
                // https://devblogs.microsoft.com/pfxteam/do-i-need-to-dispose-of-tasks/
                // https://www.cnblogs.com/heyuquan/archive/2013/02/28/2937701.html (中文)
                _task?.Dispose();

                _task = Task.Factory.StartNew(LogTask, TaskCreationOptions.LongRunning);
            }
        }

        public static void Stop()
        {
            _stop = true;
        }

        private static void DoWriteLog()
        {
            try
            {
                LogWriteLock.EnterWriteLock();
                File.AppendAllText(Path.Combine(LogDirectory, LogFile), SbLog.ToString());
            }
            finally
            {
                SbLog.Clear();
                _currentLogCount = 0;
                LogWriteLock.ExitWriteLock();
            }
        }

        private static void WriteDebugInfo(string debugInfo)
        {
            if (ShowDebugInfo)
            {
                Trace.WriteLine($"{LIGHTLOG_DEBUG_PREFIX}{debugInfo}");
            }
        }

        public static void Info(string logText)
        {
            WriteDebugInfo($"LogTime: {DateTime.Now.ToString(DEBUG_TIME_FORMAT)} - ThreadId: {Thread.CurrentThread.ManagedThreadId}");

            LogBlockingCollection.Add(logText);
        }
    }
}
