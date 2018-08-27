﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Webhook;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SiteLinkSynchronizer.Configuration;
using WikiClientLibrary;

namespace SiteLinkSynchronizer
{

    public static class DiscordWebhookLoggerExtensions
    {

        public static ILoggingBuilder AddDiscordWebhookLogger(this ILoggingBuilder builder)
        {
            builder.Services.AddSingleton<ILoggerProvider>(sp =>
                {
                    var config = sp.GetRequiredService<IOptions<DiscordWebhookLoggerConfig>>()?.Value;
                    if (config?.Token == null) return NullLoggerProvider.Instance;
                    return new DiscordWebhookLoggerProvider(LogLevel.Debug, config.Id, config.Token);
                }
            );
            return builder;
        }

    }

    public class DiscordWebhookLoggerProvider : ILoggerProvider
    {

        private readonly LogLevel minimumLevel;
        private readonly ulong webhookId;
        private readonly Task workerTask;
        private readonly BlockingCollection<string> impendingMessages = new BlockingCollection<string>(1024);
        private readonly SemaphoreSlim impendingMessagesSemaphore = new SemaphoreSlim(0, 1024);
        private readonly CancellationTokenSource disposalCts = new CancellationTokenSource();

        public DiscordWebhookLoggerProvider(LogLevel minimumLevel, ulong id, string token)
        {
            this.minimumLevel = minimumLevel;
            this.webhookId = id;
            workerTask = WorkerAsync(token, disposalCts.Token);
        }

        private async Task WorkerAsync(string token, CancellationToken ct)
        {
            using (impendingMessagesSemaphore)
            using (var client = new DiscordWebhookClient(webhookId, token))
            {
                try
                {
                    while (!ct.IsCancellationRequested) 
                    {
                        await impendingMessagesSemaphore.WaitAsync(ct);
                        var message = impendingMessages.Take();
                        await client.SendMessageAsync(message);
                    }
                } catch (OperationCanceledException)
                {
                    // cancelled from WaitAsync
                }
                // Cleanup
                while (impendingMessages.TryTake(out var message))
                {
                    await client.SendMessageAsync(message);
                }
            }
        }

        internal void QueueMessage(string message)
        {
            if (disposalCts.IsCancellationRequested) return;

            // Propagate worker's exceptions, if any.
            if (workerTask.IsFaulted) workerTask.GetAwaiter().GetResult();

            lock (impendingMessages)
                impendingMessages.Add(message);
            try
            {
                impendingMessagesSemaphore.Release();
            }
            catch (ObjectDisposedException)
            {
                // In case impendingMessagesSemaphore has been released.
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            disposalCts.Cancel();
            try
            {
                workerTask.Wait(15 * 1000);
            }
            catch (Exception)
            {
            }
        }

        /// <inheritdoc />
        public ILogger CreateLogger(string categoryName)
        {
            return new DiscordWebhookLogger(this, categoryName, minimumLevel);
        }
    }

    public class DiscordWebhookLogger : ILogger, IDisposable
    {
        private readonly DiscordWebhookLoggerProvider owner;
        private readonly LogLevel minimumLevel;

        internal DiscordWebhookLogger(DiscordWebhookLoggerProvider owner, string name, LogLevel minimumLevel)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            this.minimumLevel = minimumLevel;
            Name = name;
        }

        public string Name { get; }

        /// <inheritdoc />
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (formatter == null) throw new ArgumentNullException(nameof(formatter));
            if (logLevel < minimumLevel) return;
            var message = formatter(state, exception);
            if (string.IsNullOrEmpty(message)) return;
            var sb = new StringBuilder();
            switch (logLevel)
            {
                case LogLevel.Trace:
                    sb.Append("TRC"); break;
                case LogLevel.Debug:
                    sb.Append("DBG"); break;
                case LogLevel.Information:
                    sb.Append("INF"); break;
                case LogLevel.Warning:
                    sb.Append("**WRN**"); break;
                case LogLevel.Error:
                    sb.Append("**ERR**"); break;
                case LogLevel.Critical:
                    sb.Append("**CRT**"); break;
                case LogLevel.None:
                    sb.Append("NON"); break;
            }
            sb.Append(": ");
            var leftMargin = sb.Length;
            sb.Append(Name);
            sb.AppendLine();
            sb.Append(' ', leftMargin);
            sb.Append(message);
            sb.Replace(Directory.GetCurrentDirectory(), "$PWD");
            if (exception != null)
            {
                sb.AppendLine();
                sb.Append(' ', leftMargin);
                sb.Append(exception);
            }
            owner.QueueMessage(sb.ToString());
        }

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel >= minimumLevel;
        }

        /// <inheritdoc />
        public IDisposable BeginScope<TState>(TState state)
        {
            return NullDisposable.Instance;
        }

        /// <inheritdoc />
        public void Dispose()
        {
        }

        private class NullDisposable : IDisposable
        {
            public static readonly IDisposable Instance = new NullDisposable();

            /// <inheritdoc />
            public void Dispose()
            {
            }
        }

    }
}
