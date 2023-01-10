#region Copyright & License Information
/*
 * Copyright 2007-2020 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Effects;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	public class VulturePowerInfo : SupportPowerInfo
	{
		[ActorReference(typeof(AircraftInfo))]
		public readonly string UnitType = "badr.bomber";
		public readonly int SquadSize = 1;
		public readonly WVec SquadOffset = new WVec(-1536, 1536, 0);

		public readonly int QuantizedFacings = 32;
		public readonly WDist Cordon = new WDist(5120);

		[Desc("The minimal distance from the target area when the ares is revealed")]
		public readonly WDist OperationAreaRadius = new WDist(12288);

		public readonly int RevealDuration = 400;

		[ActorReference]
		[Desc("Actor to spawn when the aircraft start attacking")]
		public readonly string CameraActor = null;

		[Desc("Amount of time to keep the camera alive after the aircraft have finished attacking")]
		public readonly int CameraRemoveDelay = 25;

		[Desc("Weapon range offset to apply during the beacon clock calculation")]
		public readonly WDist BeaconDistanceOffset = WDist.FromCells(6);

		public override object Create(ActorInitializer init) { return new VulturePower(init.Self, this); }
	}

	public class VulturePower : SupportPower
	{
		readonly VulturePowerInfo info;
		OperationVulture operation;

		public VulturePower(Actor self, VulturePowerInfo info)
			: base(self, info)
		{
			this.info = info;
		}

		public override void SelectTarget(Actor self, string order, SupportPowerManager manager)
		{
			base.SelectTarget(self, order, manager);
		}

		public override void Activate(Actor self, Order order, SupportPowerManager manager)
		{
			base.Activate(self, order, manager);
			SendVultures(self, order.Target.CenterPosition);
		}

		public void SendVultures(Actor self, WPos target)
		{
			operation = new OperationVulture(
				target,
				self.CenterPosition,
				info.OperationAreaRadius,
				info.Cordon,
				info.UnitType,
				self.Owner,
				info.RevealDuration,
				self.World);

			Actor camera = null;
			Beacon beacon = null;
			var aircraftInRange = new Dictionary<Actor, bool>();

			Action<Actor> onEnterRange = a =>
			{
				// Spawn a camera and remove the beacon when the first plane enters the target area
				if (info.CameraActor != null && camera == null && !aircraftInRange.Any(kv => kv.Value))
				{
					self.World.AddFrameEndTask(w =>
					{
						camera = w.CreateActor(info.CameraActor, new TypeDictionary
						{
							new LocationInit(self.World.Map.CellContaining(target)),
							new OwnerInit(self.Owner),
						});
					});
				}

				RemoveBeacon(beacon);

				aircraftInRange[a] = true;
			};

			Action<Actor> onExitRange = a =>
			{
				aircraftInRange[a] = false;

				// Remove the camera when the final plane leaves the target area
				if (!aircraftInRange.Any(kv => kv.Value))
					RemoveCamera(camera);
			};

			Action<Actor> onRemovedFromWorld = a =>
			{
				aircraftInRange[a] = false;

				// Checking for attack range is not relevant here because
				// aircraft may be shot down before entering the range.
				// If at the map's edge, they may be removed from world before leaving.
				if (aircraftInRange.All(kv => !kv.Key.IsInWorld))
				{
					RemoveCamera(camera);
					RemoveBeacon(beacon);
				}
			};

			self.World.AddFrameEndTask(w =>
			{
				PlayLaunchSounds();

				operation.SendVultures(
					info.SquadSize,
					info.SquadOffset,
					onEnterRange,
					onExitRange,
					onRemovedFromWorld);

				if (Info.DisplayBeacon)
				{
					var distance = (target - operation.CheckpointStart).HorizontalLength;

					var distanceTestActor = operation.Vultures[0];

					beacon = new Beacon(
						self.Owner,
						target,
						Info.BeaconPaletteIsPlayerPalette,
						Info.BeaconPalette,
						Info.BeaconImage,
						Info.BeaconPoster,
						Info.BeaconPosterPalette,
						Info.BeaconSequence,
						Info.ArrowSequence,
						Info.CircleSequence,
						Info.ClockSequence,
						() => 1 - ((distanceTestActor.CenterPosition - target).HorizontalLength - info.BeaconDistanceOffset.Length) * 1f / distance,
						Info.BeaconDelay);

					w.Add(beacon);
				}
			});
		}

		void RemoveCamera(Actor camera)
		{
			if (camera == null)
				return;

			camera.QueueActivity(new Wait(info.CameraRemoveDelay));
			camera.QueueActivity(new RemoveSelf());
			camera = null;
		}

		void RemoveBeacon(Beacon beacon)
		{
			if (beacon == null)
				return;

			Self.World.AddFrameEndTask(w =>
			{
				w.Remove(beacon);
				beacon = null;
			});
		}
	}
}
