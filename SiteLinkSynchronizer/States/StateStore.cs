using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Options;
using SiteLinkSynchronizer.Configuration;
using SQLite;

namespace SiteLinkSynchronizer.States
{
    public sealed class StateStore : IDisposable
    {

        public StateStore(IOptions<StateStoreConfig> stateStoreConfig)
        {
            Connection = new SQLiteConnection(stateStoreConfig.Value.FileName);
            InitializeStorage();
        }

        public SQLiteConnection Connection { get; }

        private void InitializeStorage()
        {
            Connection.CreateTable<ClientSiteTrace>();
        }

        public void LeaveTrace(string site, DateTime nextStartTime, int? lastLogId = null)
        {
            var trace = Connection.Table<ClientSiteTrace>().FirstOrDefault(t => t.Site == site);
            if (trace == null)
            {
                trace = new ClientSiteTrace
                {
                    Site = site,
                    NextStartTime = nextStartTime,
                    LastLogId = lastLogId ?? -1
                };
                Connection.Insert(trace);
            }
            else
            {
                trace.NextStartTime = nextStartTime;
                if (lastLogId != null)
                    trace.LastLogId = lastLogId.Value;
                Connection.Update(trace);
            }
        }

        public ClientSiteTrace GetTrace(string site)
        {
            return Connection.Table<ClientSiteTrace>().FirstOrDefault(t => t.Site == site);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Connection.Dispose();
        }
    }
}
