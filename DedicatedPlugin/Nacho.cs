using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Game.Components;
using System.Collections.Generic;
using VRage.Game.Entity;
using VRage.Game;
using VRageMath;
using VRage.ObjectBuilders;
using SpaceEngineers.Game.ModAPI;
using Sandbox.Game.Entities.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using IMyLandingGear = SpaceEngineers.Game.ModAPI.Ingame.IMyLandingGear;
using IMyShipMergeBlock = SpaceEngineers.Game.ModAPI.Ingame.IMyShipMergeBlock;
using System;

namespace Nacho
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class Nacho : MySessionComponentBase
    {
        private bool initialized = false;

        public override void LoadData()
        {
            base.LoadData();
            MyAPIGateway.Utilities.MessageEntered += OnMessageEntered;
        }

        public override void BeforeStart()
        {
            base.BeforeStart();
            MyAPIGateway.Utilities.ShowMessage("SmallGridRemover", "BeforeStart called.");
            MyAPIGateway.Session.OnSessionReady += OnSessionReady;
        }

        protected override void UnloadData()
        {
            base.UnloadData();
            MyAPIGateway.Utilities.ShowMessage("SmallGridRemover", "UnloadData called.");
            MyAPIGateway.Session.OnSessionReady -= OnSessionReady;
            MyAPIGateway.Utilities.MessageEntered -= OnMessageEntered;
        }

        private void OnSessionReady()
        {
            MyAPIGateway.Utilities.ShowMessage("SmallGridRemover", "OnSessionReady called.");
            if (!initialized)
            {
                initialized = true;
            }
        }

        private void OnMessageEntered(string messageText, ref bool sendToOthers)
        {
            if (messageText.Equals("/cleanupgrid", StringComparison.OrdinalIgnoreCase))
            {
                MyAPIGateway.Utilities.ShowMessage("SmallGridRemover", "Cleanup command received.");
                CleanupSmallGrids();
            }
        }

        private void CleanupSmallGrids()
        {
            MyAPIGateway.Utilities.ShowMessage("SmallGridRemover", "CleanupSmallGrids called.");
            HashSet<IMyEntity> entities = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(entities, CollectSmallGrids);

            MyAPIGateway.Utilities.ShowMessage("SmallGridRemover", $"Entities count: {entities.Count}");

            foreach (var entity in entities)
            {
                if (entity is IMyCubeGrid grid)
                {
                    MyAPIGateway.Utilities.ShowMessage("SmallGridRemover", $"Processing grid: {grid.DisplayName}");
                    bool isAttached = IsGridAttached(grid);
                    if (!isAttached)
                    {
                        MyAPIGateway.Utilities.ShowMessage("Small Grid Removal", $"Grid: {grid.DisplayName} is being removed.");
                        grid.Close();
                    }
                    else
                    {
                        MyAPIGateway.Utilities.ShowMessage("Small Grid Attachment", $"Grid: {grid.DisplayName} Is Attached: {isAttached}");
                    }
                }
                else
                {
                    MyAPIGateway.Utilities.ShowMessage("SmallGridRemover", "Entity is not a grid.");
                }
            }
        }

        private bool CollectSmallGrids(IMyEntity entity)
        {
            if (entity is IMyCubeGrid grid)
            {
                MyAPIGateway.Utilities.ShowMessage("SmallGridRemover", $"Found grid: {grid.DisplayName}, size: {grid.GridSizeEnum}, blocks count: {GetBlocksCount(grid)}");
                if (grid.GridSizeEnum == MyCubeSize.Small && GetBlocksCount(grid) <= 3)
                {
                    return true;
                }
            }
            return false;
        }

        private int GetBlocksCount(IMyCubeGrid grid)
        {
            List<IMySlimBlock> blocks = new List<IMySlimBlock>();
            grid.GetBlocks(blocks);
            return blocks.Count;
        }

        private bool IsGridAttached(IMyCubeGrid grid)
        {
            List<IMySlimBlock> blocks = new List<IMySlimBlock>();
            grid.GetBlocks(blocks);

            foreach (var block in blocks)
            {
                if (block.FatBlock is IMyLandingGear landingGear)
                {
                    MyAPIGateway.Utilities.ShowMessage("SmallGridRemover", $"Checking landing gear: {landingGear.CustomName}, IsLocked: {landingGear.IsLocked}");
                    if (landingGear.IsLocked)
                    {
                        return true;
                    }
                }
                else if (block.FatBlock is Sandbox.ModAPI.IMyShipConnector connector)
                {
                    MyAPIGateway.Utilities.ShowMessage("SmallGridRemover", $"Checking connector: {connector.CustomName}, Status: {connector.Status}");
                    if (connector.Status == Sandbox.ModAPI.Ingame.MyShipConnectorStatus.Connected)
                    {
                        return true;
                    }
                }
                else if (block.FatBlock is IMyShipMergeBlock mergeBlock)
                {
                    MyAPIGateway.Utilities.ShowMessage("SmallGridRemover", $"Checking merge block: {mergeBlock.CustomName}, IsConnected: {mergeBlock.IsConnected}");
                    if (mergeBlock.IsConnected)
                    {
                        return true;
                    }
                }
                else if (block.FatBlock is Sandbox.ModAPI.IMyMechanicalConnectionBlock mechanicalBlock)
                {
                    MyAPIGateway.Utilities.ShowMessage("SmallGridRemover", $"Checking mechanical block: {mechanicalBlock.CustomName}, TopGrid: {mechanicalBlock.TopGrid}");
                    if (mechanicalBlock.TopGrid != null)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}