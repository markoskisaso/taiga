/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSim Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using OpenMetaverse;
using log4net;
using OpenSim.Framework;
using OpenSim.Framework.Console;
using OpenSim.Framework.Communications.Cache;
using OpenSim.Region.Framework.Interfaces;

namespace OpenSim.Region.Framework.Scenes
{
    public abstract class SceneBase : IScene
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        #region Events

        public event restart OnRestart;

        #endregion

        #region Fields
        
        /// <value>
        /// All the region modules attached to this scene.
        /// </value>
        public Dictionary<string, IRegionModule> Modules
        {
            get { return m_modules; }
        }
        protected Dictionary<string, IRegionModule> m_modules = new Dictionary<string, IRegionModule>();

        /// <value>
        /// The module interfaces available from this scene.
        /// </value>
        protected Dictionary<Type, List<object>> ModuleInterfaces = new Dictionary<Type, List<object>>();

        protected Dictionary<string, object> ModuleAPIMethods = new Dictionary<string, object>();
        
        /// <value>
        /// The module commanders available from this scene
        /// </value>
        protected Dictionary<string, ICommander> m_moduleCommanders = new Dictionary<string, ICommander>();
        
        /// <value>
        /// The module commands available for this scene
        /// </value>
        protected Dictionary<string, ICommand> m_moduleCommands = new Dictionary<string, ICommand>();        
        
        /// <value>
        /// Registered classes that are capable of creating entities.
        /// </value>
        protected Dictionary<PCode, IEntityCreator> m_entityCreators = new Dictionary<PCode, IEntityCreator>();                

        /// <summary>
        /// The last allocated local prim id.  When a new local id is requested, the next number in the sequence is
        /// dispensed.
        /// </summary>
        protected uint m_lastAllocatedLocalId = 720000;

        private readonly Mutex _primAllocateMutex = new Mutex(false);
        
        private readonly ClientManager m_clientManager = new ClientManager();

        public ClientManager ClientManager
        {
            get { return m_clientManager; }
        }

        protected ulong m_regionHandle;
        protected string m_regionName;
        protected RegionInfo m_regInfo;

        //public TerrainEngine Terrain;
        public ITerrainChannel Heightmap;

        /// <value>
        /// Allows retrieval of land information for this scene.
        /// </value>
        public ILandChannel LandChannel;

        /// <value>
        /// Manage events that occur in this scene (avatar movement, script rez, etc.).  Commonly used by region modules
        /// to subscribe to scene events.
        /// </value>
        public EventManager EventManager
        {
            get { return m_eventManager; }
        }
        protected EventManager m_eventManager;

        protected ScenePermissions m_permissions;
        public ScenePermissions Permissions
        {
            get { return m_permissions; }
        }

        protected string m_datastore;

        private IAssetCache m_assetCache;

        public IAssetCache AssetCache
        {
            get { return m_assetCache; }
            set { m_assetCache = value; }
        }

        protected RegionStatus m_regStatus;

        public RegionStatus Region_Status
        {
            get { return m_regStatus; }
            set { m_regStatus = value; }
        }

        #endregion

        #region Update Methods

        /// <summary>
        /// Normally called once every frame/tick to let the world preform anything required (like running the physics simulation)
        /// </summary>
        public abstract void Update();

        #endregion

        #region Terrain Methods

        /// <summary>
        /// Loads the World heightmap
        /// </summary>
        public abstract void LoadWorldMap();

        /// <summary>
        /// Send the region heightmap to the client
        /// </summary>
        /// <param name="RemoteClient">Client to send to</param>
        public virtual void SendLayerData(IClientAPI RemoteClient)
        {
            RemoteClient.SendLayerData(Heightmap.GetFloatsSerialised());
        }

        #endregion

        #region Add/Remove Agent/Avatar

        /// <summary>
        /// Register the new client with the scene.  The client starts off as a child agent - the later agent crossing
        /// will promote it to a root agent during login.
        /// </summary>
        /// <param name="client"></param
        public abstract void AddNewClient(IClientAPI client);

        /// <summary>
        /// Remove a client from the scene
        /// </summary>
        /// <param name="agentID"></param>
        public abstract void RemoveClient(UUID agentID);

        public abstract void CloseAllAgents(uint circuitcode);

        #endregion

        /// <summary>
        ///
        /// </summary>
        /// <returns></returns>
        public virtual RegionInfo RegionInfo
        {
            get { return m_regInfo; }
        }

        #region admin stuff

        /// <summary>
        /// Region Restart - Seconds till restart.
        /// </summary>
        /// <param name="seconds"></param>
        public virtual void Restart(int seconds)
        {
            m_log.Error("[REGION]: passing Restart Message up the namespace");
            restart handlerPhysicsCrash = OnRestart;
            if (handlerPhysicsCrash != null)
                handlerPhysicsCrash(RegionInfo);
        }

        public virtual bool PresenceChildStatus(UUID avatarID)
        {
            return false;
        }
        
        public abstract bool OtherRegionUp(RegionInfo thisRegion);

        public virtual string GetSimulatorVersion()
        {
            return "OpenSimulator Server";
        }

        #endregion

        #region Shutdown

        /// <summary>
        /// Tidy before shutdown
        /// </summary>
        public virtual void Close()
        {
            // Shut down all non shared modules.
            foreach (IRegionModule module in Modules.Values)
            {
                if (!module.IsSharedModule)
                {
                    module.Close();
                }
            }
            Modules.Clear();
            
            try
            {
                EventManager.TriggerShutdown();
            }
            catch (Exception e)
            {
                m_log.Error("[SCENE]: SceneBase.cs: Close() - Failed with exception " + e.ToString());
            }
        }

        #endregion

        /// <summary>
        /// Returns a new unallocated local ID
        /// </summary>
        /// <returns>A brand new local ID</returns>
        protected internal uint AllocateLocalId()
        {
            uint myID;

            _primAllocateMutex.WaitOne();
            myID = ++m_lastAllocatedLocalId;
            _primAllocateMutex.ReleaseMutex();

            return myID;
        }        
        
        #region Module Methods

        /// <summary>
        /// Add a module to this scene.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="module"></param>
        public void AddModule(string name, IRegionModule module)
        {
            if (!Modules.ContainsKey(name))
            {
                Modules.Add(name, module);
            }
        }

        /// <summary>
        /// Register a module commander.
        /// </summary>
        /// <param name="commander"></param>
        public void RegisterModuleCommander(ICommander commander)
        {
            lock (m_moduleCommanders)
            {
                m_moduleCommanders.Add(commander.Name, commander);
                
                lock (m_moduleCommands)
                {
                    foreach (ICommand command in commander.Commands.Values)
                    {
                        if (m_moduleCommands.ContainsKey(command.Name))
                        {
                            m_log.ErrorFormat(
                                "[MODULES]: Module commander {0} tried to register the command {1} which has already been registered",
                                commander.Name, command.Name);
                            continue;
                        }
                            
                        m_moduleCommands[command.Name] = command;
                    }                    
                }
            }
        }
        
        /// <summary>
        /// Get an available module command
        /// </summary>
        /// <param name="commandName"></param>
        /// <returns>The command if it was found, null if no command is available with that name</returns>
        public ICommand GetCommand(string commandName)
        {
            lock (m_moduleCommands)
            {
                if (m_moduleCommands.ContainsKey(commandName))
                    return m_moduleCommands[commandName];
                else
                    return null;
            }
        }

        /// <summary>
        /// Get a module commander
        /// </summary>
        /// <param name="name"></param>
        /// <returns>The module commander, null if no module commander with that name was found</returns>
        public ICommander GetCommander(string name)
        {
            lock (m_moduleCommanders)
            {
                if (m_moduleCommanders.ContainsKey(name))
                    return m_moduleCommanders[name];
            }
            
            return null;
        }

        public Dictionary<string, ICommander> GetCommanders()
        {
            return m_moduleCommanders;
        }

        /// <summary>
        /// Register an interface to a region module.  This allows module methods to be called directly as
        /// well as via events.  If there is already a module registered for this interface, it is not replaced
        /// (is this the best behaviour?)
        /// </summary>
        /// <param name="mod"></param>
        public void RegisterModuleInterface<M>(M mod)
        {
            if (!ModuleInterfaces.ContainsKey(typeof(M)))
            {
                List<Object> l = new List<Object>();
                l.Add(mod);
                ModuleInterfaces.Add(typeof(M), l);

                if (mod is IEntityCreator)
                {
                    IEntityCreator entityCreator = (IEntityCreator)mod;
                    foreach (PCode pcode in entityCreator.CreationCapabilities)
                    {
                        m_entityCreators[pcode] = entityCreator;
                    }
                }
            }
        }

        public void StackModuleInterface<M>(M mod)
        {
            List<Object> l;
            if (ModuleInterfaces.ContainsKey(typeof(M)))
                l = ModuleInterfaces[typeof(M)];
            else
                l = new List<Object>();

            if (l.Contains(mod))
                return;

            l.Add(mod);

            if (mod is IEntityCreator)
            {
                IEntityCreator entityCreator = (IEntityCreator)mod;
                foreach (PCode pcode in entityCreator.CreationCapabilities)
                {
                    m_entityCreators[pcode] = entityCreator;
                }
            }

            ModuleInterfaces[typeof(M)] = l;
        }

        /// <summary>
        /// For the given interface, retrieve the region module which implements it.
        /// </summary>
        /// <returns>null if there is no registered module implementing that interface</returns>
        public T RequestModuleInterface<T>()
        {
            if (ModuleInterfaces.ContainsKey(typeof(T)))
            {
                return (T)ModuleInterfaces[typeof(T)][0];
            }
            else
            {
                return default(T);
            }
        }

        /// <summary>
        /// For the given interface, retrieve an array of region modules that implement it.
        /// </summary>
        /// <returns>an empty array if there are no registered modules implementing that interface</returns>
        public T[] RequestModuleInterfaces<T>()
        {
            if (ModuleInterfaces.ContainsKey(typeof(T)))
            {
                List<T> ret = new List<T>();

                foreach (Object o in ModuleInterfaces[typeof(T)])
                    ret.Add((T)o);
                return ret.ToArray();
            }
            else
            {
                return new T[] { default(T) };
            }
        }
        
        #endregion
        
        /// <summary>
        /// Shows various details about the sim based on the parameters supplied by the console command in openSimMain.
        /// </summary>
        /// <param name="showParams">What to show</param>        
        public virtual void Show(string[] showParams)
        {
            switch (showParams[0])
            {
                case "modules":
                    m_log.Error("The currently loaded modules in " + RegionInfo.RegionName + " are:");
                    foreach (IRegionModule module in Modules.Values)
                    {
                        if (!module.IsSharedModule)
                        {
                            m_log.Error("Region Module: " + module.Name);
                        }
                    }
                    break;
            }
        }        

        public void AddCommand(object mod, string command, string shorthelp, string longhelp, CommandDelegate callback)
        {
            if (MainConsole.Instance == null)
                return;

            string modulename = String.Empty;
            bool shared = false;

            if (mod != null)
            {
                if (!(mod is IRegionModule))
                    throw new Exception("AddCommand module parameter must be IRegionModule");
                IRegionModule module = (IRegionModule)mod;
                modulename = module.Name;
                shared = module.IsSharedModule;
            }

            MainConsole.Instance.Commands.AddCommand(modulename, shared, command, shorthelp, longhelp, callback);
        }
    }
}