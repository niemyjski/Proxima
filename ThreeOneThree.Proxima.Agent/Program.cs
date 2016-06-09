﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using PowerArgs;
using Quartz;
using ThreeOneThree.Proxima.Core;
using ThreeOneThree.Proxima.Core.Entities;
using Topshelf;
using Topshelf.Quartz;

namespace ThreeOneThree.Proxima.Agent
{
    class Program
    {
        static private List<string> TopshelfParameters = new List<string> { "install" ,"start","stop","uninstall"};
        private static Logger logger = LogManager.GetCurrentClassLogger();

        static void Main(string[] args)
        {
            using(Repository repo = new Repository())
            {
                Singleton.Instance.Servers = repo.All<Server>().ToList();

                var currentServer = Singleton.Instance.Servers.FirstOrDefault(f => f.MachineName == Environment.MachineName.ToLowerInvariant());
                if (currentServer == null)
                {
                    currentServer = new Server(Environment.MachineName);
                    repo.Add(currentServer);
                    Singleton.Instance.Servers.Add(currentServer);
                }
                logger.Debug(string.Format("Current server = {0} ({1})", currentServer.MachineName, currentServer.Id));
                Singleton.Instance.CurrentServer = currentServer;
                Singleton.Instance.DestinationMountpoints = repo.Many<SyncMountpoint>(f => f.DestinationServer == currentServer.Id).ToList();
                Singleton.Instance.SourceMountpoints = repo.Many<MonitoredMountpoint>(f => f.Server == currentServer.Id).ToList();

            }

            Singleton.Instance.SourceMountpoints.ForEach(f=>logger.Debug("Source: " + f.ToString()));
            Singleton.Instance.DestinationMountpoints.ForEach(f=>logger.Debug("Destination: " + f.Path + " // " + f.Mountpoint));


            if (args.Length == 0 || TopshelfParameters.Contains(args[0].ToLowerInvariant()))
            {
                Host host = HostFactory.New(x =>
                {
                    x.Service<Service>(service =>
                    {
                        service.ConstructUsing(f => new Service());

                        service.WhenStarted((a, control) => a.Start(control));
                        service.WhenStopped((a, control) => a.Stop(control));

                        service.ScheduleQuartzJob(b => b.WithJob(() => JobBuilder.Create<USNJournalSync>().Build())
                          .AddTrigger(() => TriggerBuilder.Create().WithSimpleSchedule(builder => builder.WithMisfireHandlingInstructionFireNow().WithIntervalInSeconds(2).RepeatForever()).Build()));

                        service.ScheduleQuartzJob(b => b.WithJob(() => JobBuilder.Create<USNJournalMonitor>().Build())
                            .AddTrigger(() => TriggerBuilder.Create().WithSimpleSchedule(builder => builder.WithMisfireHandlingInstructionFireNow().WithIntervalInSeconds(2).RepeatForever()).Build()));

                    });
                    x.RunAsLocalSystem();
                    x.StartAutomaticallyDelayed();
                    x.SetDescription("The Proxima Agent that monitors the USN Journal");
                    x.SetDisplayName("ProximaAgent");
                    x.SetServiceName("ThreeOneThree.Proxima.Agent");



                });

                host.Run();
                return;
            }
            ConsoleString usageHints = new ConsoleString();
            try
            {
                
                if (args[0].ToLowerInvariant() == "source")
                {
                    Args.InvokeAction<ConsoleCommands.Source>(args.Skip(1).ToArray());
                    
                    usageHints = ArgUsage.GenerateUsageFromTemplate<ConsoleCommands.Source>();
                }
                else if (args[0].ToLowerInvariant() == "dest")
                {
                    Args.InvokeAction<ConsoleCommands.Dest>(args.Skip(1).ToArray());
                    usageHints = ArgUsage.GenerateUsageFromTemplate<ConsoleCommands.Dest>();
                }
                else
                {
                    Console.WriteLine("Missing type of config - source or dest");
                    return;
                }
            }
            catch (ArgException ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(usageHints);
            }


        }
    }
}
