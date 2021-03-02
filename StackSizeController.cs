﻿using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("Stack Size Controller", "AnExiledGod", "3.0.0")]
    [Description("Allows configuration of most items max stack size.")]
    class StackSizeController : CovalencePlugin
    {
        private ConfigData _config;
        private ItemIndex _data;

        private void Init()
        {
            _config = Config.ReadObject<ConfigData>();
            _data = Interface.Oxide.DataFileSystem.ReadObject<ItemIndex>(nameof(StackSizeController)) ??
                    new ItemIndex();

            if (_config.IsNull<ConfigData>())
            {
                LoadDefaultConfig();
            }
            
            EnsureConfigIntegrity();
            SaveConfig();
            
            if (_data.IsUnityNull() || _data.ItemCategories.IsUnityNull())
            {
                _data.VersionNumber = Version;
                
                CreateItemIndex();
                SaveData();
            }
            else
            {
                UpdateItemIndex();
            }

            AddCovalenceCommand("stacksizecontroller.regendatafile", nameof(RegenerateDataFileCommand));
            AddCovalenceCommand("stacksizecontroller.setstack", nameof(SetStackCommand));
            AddCovalenceCommand("stacksizecontroller.setstackcat", nameof(SetStackCategoryCommand));
            AddCovalenceCommand("stacksizecontroller.setallstacks", nameof(SetAllStacksCommand));
            AddCovalenceCommand("stacksizecontroller.itemsearch", nameof(ItemSearchCommand));
            AddCovalenceCommand("stacksizecontroller.listcategories", nameof(ListCategoriesCommand));

            SetStackSizes();
        }

        private void Unloaded()
        {
            SaveConfig();
            SaveData();
        }

        #region Configuration
        
        private class ConfigData
        {
            public bool AllowStackingItemsWithDurability = true;
            public bool HidePrefixWithPluginNameInMessages;

            public int GlobalStackMultiplier = 1;
            public Dictionary<string, int> CategoryStackMultipliers = GetCategoriesAndDefaults();
            public Dictionary<int, int> IndividualItemStackMultipliers = new Dictionary<int, int>();
            public Dictionary<int, int> IndividualItemStackHardLimits = new Dictionary<int, int>();
            
            public VersionNumber VersionNumber;
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config);
        }

        protected override void LoadDefaultConfig()
        {
            ConfigData defaultConfig = GetDefaultConfig();
            defaultConfig.VersionNumber = Version;
            
            Config.WriteObject(defaultConfig);
            
            _config = Config.ReadObject<ConfigData>();
        }

        private void EnsureConfigIntegrity()
        {
            ConfigData configDefault = new ConfigData();

            if (_config.AllowStackingItemsWithDurability.IsNull<bool>()) { _config.AllowStackingItemsWithDurability = 
                configDefault.AllowStackingItemsWithDurability; }
            if (_config.HidePrefixWithPluginNameInMessages.IsNull<bool>()) { _config.HidePrefixWithPluginNameInMessages =
                configDefault.HidePrefixWithPluginNameInMessages; }
        }

        private ConfigData GetDefaultConfig()
        {
            return new ConfigData();
        }
        
        #endregion
        
        #region Localization
        
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NotEnoughArguments"] = "This command requires {0} arguments.",
                ["InvalidItemShortnameOrId"] =
                    "Item shortname or id is incorrect. Try stacksizecontroller.itemsearch [partial item name]",
                ["InvalidCategory"] = "Category not found. Try stacksizecontroller.listcategories",
                ["OperationSuccessful"] = "Operation completed successfully.",
            }, this);
        }

        private string GetMessage(string key, string playerId)
        {
            if (_config.HidePrefixWithPluginNameInMessages || playerId == "server_console")
            {
                return lang.GetMessage(key, this, playerId);
            }
            
            return $"<color=#ff760d><b>[{nameof(StackSizeController)}]</b></color> " +
                   lang.GetMessage(key, this, playerId);
        }

        #endregion
        
        #region Data Handling

        private class ItemIndex
        {
            public Dictionary<string, List<ItemInfo>> ItemCategories;
            public VersionNumber VersionNumber;
        }

        private class ItemInfo
        {
            public int ItemId;
            public string Shortname;
            public bool HasDurability;
            public int VanillaStackSize;
            public int CustomStackSize;
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(nameof(StackSizeController), _data);
        }
        
        private void CreateItemIndex()
        {
            _data.ItemCategories = new Dictionary<string, List<ItemInfo>>();

            // Create categories
            foreach (string category in Enum.GetNames(typeof(ItemCategory)))
            {
                _data.ItemCategories.Add(category, new List<ItemInfo>());
            }
            
            // Iterate and categorize items
            foreach (ItemDefinition itemDefinition in ItemManager.GetItemDefinitions())
            {
                _data.ItemCategories[itemDefinition.category.ToString()].Add(
                    new ItemInfo
                    {
                        ItemId = itemDefinition.itemid,
                        Shortname = itemDefinition.shortname,
                        HasDurability = itemDefinition.condition.enabled,
                        VanillaStackSize = itemDefinition.stackable,
                        CustomStackSize = itemDefinition.stackable
                    });
            }
            
            SaveData();
        }

        private void UpdateItemIndex()
        {
            foreach (ItemDefinition itemDefinition in ItemManager.GetItemDefinitions())
            {
                if (!_data.ItemCategories[itemDefinition.category.ToString()]
                    .Exists(itemInfo => itemInfo.ItemId == itemDefinition.itemid))
                {
                    _data.ItemCategories[itemDefinition.category.ToString()].Add(
                        new ItemInfo
                        {
                            ItemId = itemDefinition.itemid,
                            Shortname = itemDefinition.shortname,
                            HasDurability = itemDefinition.condition.enabled,
                            VanillaStackSize = itemDefinition.stackable,
                            CustomStackSize = itemDefinition.stackable
                        });
                }
            }
        }
        
        private ItemInfo AddItemToIndex(int itemId)
        {
            ItemDefinition itemDefinition = ItemManager.FindItemDefinition(itemId);
            
            ItemInfo item = new ItemInfo
            {
                ItemId = itemId,
                Shortname = itemDefinition.shortname,
                HasDurability = itemDefinition.condition.enabled,
                VanillaStackSize = itemDefinition.stackable,
                CustomStackSize = itemDefinition.stackable
            };
            
            _data.ItemCategories[itemDefinition.category.ToString()].Add(item);

            return item;
        }

        private ItemInfo GetIndexedItem(ItemCategory itemCategory, int itemId)
        {
            ItemInfo itemInfo = _data.ItemCategories[itemCategory.ToString()].First(item => item.ItemId == itemId) ??
                                AddItemToIndex(itemId);

            return itemInfo;
        }

        #endregion
        
        #region Commands
        
        /*
         * dumpitemlist command
         */
        private void RegenerateDataFileCommand(IPlayer player, string command, string[] args)
        {
            CreateItemIndex();
            
            player.Reply(GetMessage("OperationSuccessful", player.Id));
        }

        private void SetStackCommand(IPlayer player, string command, string[] args)
        {
            if (args.Length != 2)
            {
                player.Reply(
                    string.Format(GetMessage("NotEnoughArguments", player.Id), 2));
            }
            
            ItemDefinition itemDefinition = ItemManager.FindItemDefinition(args[0]);
            string stackSizeString = args[1];
            
            if (itemDefinition == null)
            {
                player.Reply(GetMessage("InvalidItemShortnameOrId", player.Id));

                return;
            }

            if (stackSizeString.Substring(stackSizeString.Length - 1) == "x")
            {
                if (_config.IndividualItemStackMultipliers.ContainsKey(itemDefinition.itemid))
                {
                    _config.IndividualItemStackMultipliers[itemDefinition.itemid] =
                        Convert.ToInt32(stackSizeString.TrimEnd('x'));
                    
                    SaveConfig();
                    player.Reply(GetMessage("OperationSuccessful", player.Id));

                    return;
                }
                
                _config.IndividualItemStackMultipliers.Add(itemDefinition.itemid,
                    Convert.ToInt32(stackSizeString.TrimEnd('x')));
                
                SaveConfig();
                player.Reply(GetMessage("OperationSuccessful", player.Id));

                return;
            }

            if (_config.IndividualItemStackHardLimits.ContainsKey(itemDefinition.itemid))
            {
                _config.IndividualItemStackHardLimits[itemDefinition.itemid] = Convert.ToInt32(stackSizeString.TrimEnd('x'));
                
                SaveConfig();
                player.Reply(GetMessage("OperationSuccessful", player.Id));
                
                return;
            }
            
            _config.IndividualItemStackHardLimits.Add(itemDefinition.itemid, Convert.ToInt32(stackSizeString.TrimEnd('x')));

            SaveConfig();
            player.Reply(GetMessage("OperationSuccessful", player.Id));
        }

        private void SetAllStacksCommand(IPlayer player, string command, string[] args)
        {
            if (args.Length != 1)
            {
                player.Reply(
                    string.Format(GetMessage("NotEnoughArguments", player.Id), 1));
            }

            foreach (string category in _config.CategoryStackMultipliers.Keys.ToList())
            {
                _config.CategoryStackMultipliers[category] = Convert.ToInt32(args[0]);
            }

            SaveConfig();
            
            player.Reply(GetMessage("OperationSuccessful", player.Id));
        }

        private void SetStackCategoryCommand(IPlayer player, string command, string[] args)
        {
            if (args.Length != 2)
            {
                player.Reply(
                    string.Format(GetMessage("NotEnoughArguments", player.Id), 2));
            }

            ItemCategory itemCategory = (ItemCategory) Enum.Parse(typeof(ItemCategory), args[0], true);

            if (itemCategory.IsNull<ItemCategory>())
            {
                player.Reply(GetMessage("InvalidCategory", player.Id));
            }
            
            string stackSizeString = args[1];
            _config.CategoryStackMultipliers[itemCategory.ToString()] = Convert.ToInt32(stackSizeString.TrimEnd('x'));

            SaveConfig();
            
            player.Reply(GetMessage("OperationSuccessful", player.Id));
        }

        private void ItemSearchCommand(IPlayer player, string command, string[] args)
        {
            if (args.Length != 1)
            {
                player.Reply(
                    string.Format(GetMessage("NotEnoughArguments", player.Id), 1));
            }
            
            List<ItemDefinition> itemDefinitions = ItemManager.itemList.Where(itemDefinition =>
                    itemDefinition.displayName.english.Contains(args[0]) ||
                    itemDefinition.displayDescription.english.Contains(args[0]) ||
                    itemDefinition.shortname.Equals(args[0]) ||
                    itemDefinition.shortname.Contains(args[0]))
                .ToList();
            
            TextTable output = new TextTable();
            output.AddColumns("Unique Id", "Shortname", "Vanilla Stack", "Custom Stack");

            foreach (ItemDefinition itemDefinition in itemDefinitions)
            {
                ItemInfo itemInfo = GetIndexedItem(itemDefinition.category, itemDefinition.itemid);
                
                output.AddRow(itemDefinition.itemid.ToString(), itemDefinition.shortname, 
                    itemInfo.VanillaStackSize.ToString("N0"), itemInfo.CustomStackSize.ToString("N0"));
            }
            
            player.Reply(output.ToString());
        }

        private void ListCategoriesCommand(IPlayer player, string command, string[] args)
        {
            TextTable output = new TextTable();
            output.AddColumns("Category Name", "Items In Category");

            foreach (string category in Enum.GetNames(typeof(ItemCategory)))
            {
                output.AddRow(category, _data.ItemCategories[category].Count.ToString());
            }
            
            player.Reply(output.ToString());
        }

        #endregion

        #region Hooks
        
        private void OnServerSave()
        {
            SaveConfig();
            SaveData();
        }
        
        private object CanStackItem(Item item, Item targetItem)
        {
            if (item.GetOwnerPlayer().IsUnityNull())
            {
                return null;
            }
            
            if (
                item == targetItem ||
                item.info.stackable <= 1 ||
                item.info.itemid != targetItem.info.itemid ||
                !item.IsValid() ||
                (item.IsBlueprint() && item.blueprintTarget != targetItem.blueprintTarget) ||
                (item.hasCondition && (item.condition != item.info.condition.max || 
                                      targetItem.condition != targetItem.info.condition.max))
            )
            {
                return false;
            }

            BaseProjectile.Magazine itemMag = 
                targetItem.GetHeldEntity()?.GetComponent<BaseProjectile>()?.primaryMagazine;
            
            // Return ammo
            if (itemMag != null)
            {
                if (itemMag.contents > 0)
                {
                    item.GetOwnerPlayer().GiveItem(ItemManager.CreateByItemID(itemMag.ammoType.itemid, 
                        itemMag.contents));
                }
            }
            
            if (targetItem.GetHeldEntity()?.GetComponent<FlameThrower>() != null)
            {
                FlameThrower flameThrower = targetItem.GetHeldEntity().GetComponent<FlameThrower>();

                if (flameThrower.ammo > 0)
                {
                    item.GetOwnerPlayer().GiveItem(ItemManager.CreateByItemID(flameThrower.fuelType.itemid, 
                        flameThrower.ammo));
                }
            }
            
            if (targetItem.GetHeldEntity()?.GetComponent<Chainsaw>() != null)
            {
                Chainsaw chainsaw = targetItem.GetHeldEntity().GetComponent<Chainsaw>();

                if (chainsaw.ammo > 0)
                {
                    item.GetOwnerPlayer().GiveItem(ItemManager.CreateByItemID(chainsaw.fuelType.itemid, 
                        chainsaw.ammo));
                }
            }
            
            // Return contents
            if (targetItem.contents?.itemList.Count > 0)
            {
                foreach (Item containedItem in targetItem.contents.itemList)
                {
                    targetItem.parent.playerOwner.GiveItem(ItemManager.CreateByItemID(containedItem.info.itemid, 
                        containedItem.amount));
                }
            }

            return null;
        }
        
        private Item OnItemSplit(Item item, int amount)
        {
            item.amount -= amount;
            
            Item newItem = ItemManager.CreateByItemID(item.info.itemid, amount, item.skin);

            if (item.IsBlueprint())
            {
                newItem.blueprintTarget = item.blueprintTarget;
            }
            
            BaseProjectile.Magazine newItemMag =
                newItem.GetHeldEntity()?.GetComponent<BaseProjectile>()?.primaryMagazine;

            // Remove default ammo
            if (newItemMag != null)
            {
                newItemMag.contents = 0;
            }

            if (newItem.GetHeldEntity()?.GetComponent<FlameThrower>() != null)
            {
                newItem.GetHeldEntity().GetComponent<FlameThrower>().ammo = 0;
            }
            
            if (newItem.GetHeldEntity()?.GetComponent<Chainsaw>() != null)
            {
                newItem.GetHeldEntity().GetComponent<Chainsaw>().ammo = 0;
            }
            
            // Remove default contents (fuel, etc)
            if (newItem.contents?.itemList.Count > 0)
            {
                foreach (Item containedItem in item.contents.itemList)
                {
                    containedItem.Remove();
                }
            }
            
            item.MarkDirty();
            
            return newItem;
        }

        #endregion

        #region Helpers

        private int GetStackSize(int itemId)
        {
            return GetStackSize(ItemManager.FindItemDefinition(itemId));
        }

        private int GetStackSize(ItemDefinition itemDefinition)
        {
            ItemInfo customStackInfo = _data.ItemCategories[itemDefinition.category.ToString()]
                .Find(itemInfo => itemInfo.ItemId == itemDefinition.itemid);

            int stackable = itemDefinition.stackable;
            if (customStackInfo.VanillaStackSize != customStackInfo.CustomStackSize)
            {
                stackable = customStackInfo.CustomStackSize;
            }

            if (_config.IndividualItemStackHardLimits.ContainsKey(itemDefinition.itemid))
            {
                return _config.IndividualItemStackHardLimits[itemDefinition.itemid];
            }
            
            if (_config.IndividualItemStackMultipliers.ContainsKey(itemDefinition.itemid))
            {
                return stackable * _config.IndividualItemStackMultipliers[itemDefinition.itemid];
            }
            
            if (_config.CategoryStackMultipliers.ContainsKey(itemDefinition.category.ToString()))
            {
                return stackable * _config.CategoryStackMultipliers[itemDefinition.category.ToString()];
            }

            return stackable * _config.GlobalStackMultiplier;
        }

        private void SetStackSizes()
        {
            foreach (ItemDefinition itemDefinition in ItemManager.GetItemDefinitions())
            {
                if (itemDefinition.condition.enabled && !_config.AllowStackingItemsWithDurability)
                {
                    continue;
                }
                
                itemDefinition.stackable = GetStackSize(itemDefinition);
            }
        }

        private static Dictionary<string, int> GetCategoriesAndDefaults()
        {
            Dictionary<string, int> categoryDefaults = new Dictionary<string, int>();
            
            foreach (string category in Enum.GetNames(typeof(ItemCategory)))
            {
                categoryDefaults.Add(category, 1);
            }

            return categoryDefaults;
        }

        #endregion
    }
}