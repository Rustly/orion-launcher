﻿// Copyright (c) 2020 Pryaxis & Orion Contributors
// 
// This file is part of Orion.
// 
// Orion is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// Orion is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with Orion.  If not, see <https://www.gnu.org/licenses/>.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Orion.Core;
using Orion.Core.Events;
using Orion.Core.Events.Items;
using Orion.Core.Items;
using Orion.Core.Utils;
using Orion.Launcher.Utils;
using Serilog;

namespace Orion.Launcher.Items
{
    [Binding("orion-items", Author = "Pryaxis", Priority = BindingPriority.Lowest)]
    internal sealed class OrionItemService : IItemService, IDisposable
    {
        private readonly IEventManager _events;
        private readonly ILogger _log;
        private readonly IReadOnlyList<IItem> _items;

        private readonly object _lock = new object();

        public OrionItemService(IEventManager events, ILogger log)
        {
            Debug.Assert(events != null);
            Debug.Assert(log != null);

            _events = events;
            _log = log;

            // Note that the last item should be ignored, as it is not a real item.
            _items = new WrappedReadOnlyList<OrionItem, Terraria.Item>(
                Terraria.Main.item.AsMemory(..^1), (itemIndex, terrariaItem) => new OrionItem(itemIndex, terrariaItem));

            //OTAPI.Hooks.Item.PreSetDefaultsById = PreSetDefaultsByIdHandler;
            //OTAPI.Hooks.Item.PreUpdate = PreUpdateHandler;

            On.Terraria.Item.SetDefaults_int_bool_ItemVariant += PreSetDefaultsByIdHandler;
            On.Terraria.Item.UpdateItem += PreUpdateHandler;
        }

        public IItem this[int index] => _items[index];

        public int Count => _items.Count;

        public IEnumerator<IItem> GetEnumerator() => _items.GetEnumerator();

        public IItem Spawn(ItemStack item, Vector2f position)
        {
            Log.Debug("Spawning {Item} at {Position}", item);

            lock (_lock)
            {
                var itemIndex = Terraria.Item.NewItem(null,
                    (int)position.X, (int)position.Y, 0, 0, (int)item.Id, item.StackSize, false, (int)item.Prefix);
                Debug.Assert(itemIndex >= 0 && itemIndex < Count);

                return this[itemIndex];
            }
        }

        public void Dispose()
        {
            On.Terraria.Item.SetDefaults_int_bool_ItemVariant -= PreSetDefaultsByIdHandler;
            On.Terraria.Item.UpdateItem -= PreUpdateHandler;
        }

        [ExcludeFromCodeCoverage]
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        // =============================================================================================================
        // OTAPI hooks
        //

        private void PreSetDefaultsByIdHandler(On.Terraria.Item.orig_SetDefaults_int_bool_ItemVariant orig, Terraria.Item self, int type, bool noMatCheck, Terraria.GameContent.Items.ItemVariant variant)
        {
            Debug.Assert(self != null);

            var item = GetItem(self);
            var evt = new ItemDefaultsEvent(item) { Id = (ItemId)type };
            _events.Raise(evt, _log);
            if (evt.IsCanceled)
            {
                return;
            }

            type = (int)evt.Id;
            orig(self, type, noMatCheck, variant);
        }

        private void PreUpdateHandler(On.Terraria.Item.orig_UpdateItem orig, Terraria.Item self, int i)
        {
            Debug.Assert(self != null);
            Debug.Assert(i >= 0 && i < Count);

            // Set `whoAmI` since this is never done in the vanilla server, and we depend on this field being set in
            // `GetItem`.
            self.whoAmI = i;

            var item = this[i];
            var evt = new ItemTickEvent(item);
            _events.Raise(evt, _log);

            if (!evt.IsCanceled)
                orig(self, i);
        }

        // Gets an `IItem` instance corresponding to the given Terraria item, avoiding extra allocations if possible.
        private IItem GetItem(Terraria.Item terrariaItem)
        {
            var itemIndex = terrariaItem.whoAmI;
            Debug.Assert(itemIndex >= 0 && itemIndex < Count);

            var isConcrete = ReferenceEquals(terrariaItem, Terraria.Main.item[itemIndex]);
            return isConcrete ? this[itemIndex] : new OrionItem(terrariaItem);
        }
    }
}
