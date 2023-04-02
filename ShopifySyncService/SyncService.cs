using ShopifySyncEngine;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;

namespace ShopifySyncService
{
    public partial class SyncService : ServiceBase
    {
        public SyncService()
        {
            InitializeComponent();
        }

        protected override void OnStart(string[] args)
        {
            StartSync();
        }

        private void StartSync()
        {
            if (DateTime.Now.Hour == 22 && DateTime.Now.Minute == 45)
            {
                SyncEngine.Start();
            }
            Task.Delay(TimeSpan.FromMinutes(1)).ContinueWith(_ => StartSync());

        }

        protected override void OnStop()
        {
        }
    }
}
