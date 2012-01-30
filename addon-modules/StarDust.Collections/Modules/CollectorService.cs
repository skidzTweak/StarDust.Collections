using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Timers;
using Aurora.DataManager;
using Aurora.Framework;
using Aurora.Simulation.Base;
using Nini.Config;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Services.Interfaces;
using StarDust.Collections.Interace;

namespace StarDust.Collections
{
    public class CollectorService : ConnectorBase, IStarDustCollector, IService
    {
        private bool m_enabled;
        private IStarDustCollectorConnector m_database;
        private readonly Timer taskTimer = new Timer();
        IScheduleService m_scheduler;
        private IMoneyModule m_money;

        #region Implementation of IService

        public void Initialize(IConfigSource config, IRegistryCore registry)
        {
            Init(registry, "StardustCollector");
            if (!CheckEnabled((m_doRemoteCalls) ? "Remote" : "Local", config)) return;
            m_registry = registry;
            m_registry.RegisterModuleInterface<IStarDustCollector>(this);
        }

        public void Start(IConfigSource config, IRegistryCore registry)
        {
            m_scheduler = registry.RequestModuleInterface<IScheduleService>();
            m_money = registry.RequestModuleInterface<IMoneyModule>();
            m_database = DataManager.RequestPlugin<IStarDustCollectorConnector>();
            taskTimer.Interval = 120000;
            taskTimer.Elapsed += t_Elapsed;
            taskTimer.Enabled = m_enabled;

            m_scheduler.Register("CLASSBILL", Classified_Billing);
        }

        private object Classified_Billing(string functionName, object parameters)
        {
            try
            {
                OSDMap parms = (OSDMap)parameters;
                IDirectoryServiceConnector DSC = DataManager.RequestPlugin<IDirectoryServiceConnector>();
                Classified c = DSC.GetClassifiedByID(parms["CLASSBILL"].AsUUID());
                if (c != null)
                {
                    if (m_money != null)
                    {
                        m_money.Charge(c.CreatorUUID, c.PriceForListing, "Classified Charge -" + c.Name);
                        m_scheduler.Remove(c.CreatorUUID.ToString());
                        OSDMap temper = new OSDMap { { "CLASSBILL", OSD.FromUUID(c.ClassifiedUUID) } };
                        SchedulerItem si = new SchedulerItem("CLASSBILL", temper.ToString(), true,
                                                             new TimeSpan(0, 0, Util.ToUnixTime(DateTime.UtcNow.AddMonths(1)) - Util.ToUnixTime(DateTime.UtcNow)));
                        m_scheduler.Save(si);
                    }
                    else
                    {
                        MainConsole.Instance.Info("[CollectorService] Could not find money module.");
                    }
                }
                else
                {
                    MainConsole.Instance.Info("[CollectorService] Could not find classified, might have been deleted");
                }
            }
            catch (Exception ex)
            {
                MainConsole.Instance.Error("[CollectorService] Error charging for classifieds");
            }
            
            return "";
        }

        private void t_Elapsed(object sender, ElapsedEventArgs e)
        {
            taskTimer.Enabled = false;
            SchedulerItem temp = new SchedulerItem();
            IDirectoryServiceConnector DSC = DataManager.RequestPlugin<IDirectoryServiceConnector>();

            int startAT = 0;
            bool keepGoing = true;
            do
            {
                keepGoing = false;
                List<DirClassifiedReplyData> classifieds = DSC.FindClassifieds("", ((int)DirectoryManager.ClassifiedCategories.Any).ToString(CultureInfo.InvariantCulture),
                                (uint)DirectoryManager.ClassifiedFlags.Enabled, startAT);
                startAT += classifieds.Count;
                foreach (DirClassifiedReplyData data in classifieds)
                {
                    keepGoing = true;
                    if (m_scheduler.Exist(data.classifiedID.ToString())) continue;
                    OSDMap temper = new OSDMap { { "CLASSBILL", OSD.FromUUID(data.classifiedID) } };
                    SchedulerItem si = new SchedulerItem("CLASSBILL", temper.ToString(), true,
                                                         new TimeSpan(0, 0, Util.ToUnixTime(UnixTimeStampToDateTime((int)data.expirationDate).AddMonths(1)) - Util.ToUnixTime(DateTime.UtcNow)));
                    m_scheduler.Save(si);
                }
            } while (keepGoing);

            
            
        }

        public void FinishedStartup()
        {
            
        }

        #endregion

        #region private functions
        /// <summary>
        /// This is the same function used in stardust
        /// </summary>
        /// <param name="localOrRemote"></param>
        /// <param name="source"></param>
        /// <returns></returns>
        protected bool CheckEnabled(string localOrRemote, IConfigSource source)
        {
            // check to see if it should be enabled and then load the config
            if (source == null) throw new ArgumentNullException("source");
            IConfig economyConfig = source.Configs["StarDustCurrency"];
            m_enabled = (economyConfig != null)
                            ? (economyConfig.GetString("CurrencyConnector", "Remote") == localOrRemote)
                            : "Remote" == localOrRemote;
            return m_enabled;
        }

        private static DateTime UnixTimeStampToDateTime(int unixTimeStamp)
        {
            // Unix timestamp is seconds past epoch
            DateTime dtDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0);
            dtDateTime = dtDateTime.AddSeconds(unixTimeStamp).ToLocalTime();
            return dtDateTime;
        }
        #endregion
    }
}
