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
            taskTimer.Interval = 120000;
            taskTimer.Elapsed += t_Elapsed;
            taskTimer.Enabled = m_enabled;

            m_scheduler.Register("CLASSBILL", Classified_Billing);
        }

        public void FinishedStartup()
        {
            
        }

        #endregion

        #region Billing Events

        private object Classified_Billing(string functionName, object parameters)
        {
            try
            {
                BillingClassified bc = new BillingClassified();
                bc.FromOSD((OSDMap)OSDParser.DeserializeJson(parameters.ToString()));
                IDirectoryServiceConnector DSC = DataManager.RequestPlugin<IDirectoryServiceConnector>();
                Classified c = DSC.GetClassifiedByID(bc.ClassifiedID);
                if (c != null)
                {
                    if (((c.ClassifiedFlags & (byte)DirectoryManager.ClassifiedFlags.Enabled) == (byte)DirectoryManager.ClassifiedFlags.Enabled) && (m_money != null))
                    {
                        if (!m_money.Charge(c.CreatorUUID, c.PriceForListing, "Classified Charge -" + c.Name))
                        {
                            c.ClassifiedFlags = (byte)(c.ClassifiedFlags & ~((int)DirectoryManager.ClassifiedFlags.Enabled));
                            IProfileConnector profile = DataManager.RequestPlugin<IProfileConnector>("IProfileConnector");
                            profile.AddClassified(c);
                            m_scheduler.Remove(bc.ClassifiedID.ToString());
                        }
                    }
                    else
                        MainConsole.Instance.Info("[CollectorService] Could not find money module.");
                }
                else
                    MainConsole.Instance.Info("[CollectorService] Could not find classified, might have been deleted");
            }
            catch (Exception ex)
            {
                MainConsole.Instance.Error("[CollectorService] Error charging for classifieds", ex);
            }

            return "";
        }

        #endregion

        #region Timer

        private void t_Elapsed(object sender, ElapsedEventArgs e)
        {
            taskTimer.Enabled = false;
            IDirectoryServiceConnector DSC = DataManager.RequestPlugin<IDirectoryServiceConnector>();

            int startAT = 0;
            bool keepGoing;
            do
            {
                keepGoing = false;
                List<DirClassifiedReplyData> classifieds = DSC.FindClassifieds("", ((int)DirectoryManager.ClassifiedCategories.Any).ToString(CultureInfo.InvariantCulture),
                                (uint)DirectoryManager.ClassifiedFlags.Enabled, startAT);
                startAT += classifieds.Count;
                if (classifieds.Count >= 1) keepGoing = true;
#if(!ISWIN)
                foreach (DirClassifiedReplyData data in classifieds)
                {
                    if (m_scheduler.Exist(data.classifiedID.ToString())) continue;
                    SchedulerItem si = new SchedulerItem("CLASSBILL", OSDParser.SerializeJsonString(new BillingClassified(data.classifiedID).ToOSD()), false,
                                                         UnixTimeStampToDateTime((int)data.creationDate), 1,
                                                         RepeatType.months) { id = data.classifiedID.ToString() };
                    m_scheduler.Save(si);
                }
#else
                foreach (SchedulerItem si in from data in classifieds
                                             where !m_scheduler.Exist(data.classifiedID.ToString())
                                             select new SchedulerItem("CLASSBILL", OSDParser.SerializeJsonString(new BillingClassified(data.classifiedID).ToOSD()), false,
                                                                      UnixTimeStampToDateTime((int)data.creationDate), 1,
                                                                      RepeatType.months) { id = data.classifiedID.ToString() })
                {
                    m_scheduler.Save(si);
                }
#endif

            } while (keepGoing);
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

    public class BillingClassified : IDataTransferable
    {
        #region initializer
        public BillingClassified(UUID classifiedID)
        {
            ClassifiedID = classifiedID;
        }

        public BillingClassified()
        {
        }

        #endregion
        

        #region IDataTransferable
        /// <summary>
        ///   Serialize the module to OSD
        /// </summary>
        /// <returns></returns>
        public override OSDMap ToOSD()
        {
            return new OSDMap()
                       {
                           {"ClassifiedID", OSD.FromUUID(ClassifiedID)}
                       };
        }

        /// <summary>
        ///   Deserialize the module from OSD
        /// </summary>
        /// <param name = "map"></param>
        public override void FromOSD(OSDMap map)
        {
            ClassifiedID = map["ClassifiedID"].AsUUID();
        }
        #endregion

        #region properties

        public UUID ClassifiedID { get; set; }

        #endregion
    }
}
