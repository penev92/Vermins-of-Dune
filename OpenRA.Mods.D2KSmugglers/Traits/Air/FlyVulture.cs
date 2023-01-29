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
using OpenRA.Activities;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Effects;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Mods.D2KSmugglers.Effects;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	public class FlyVultureInfo : AttackBaseInfo
	{
		[Desc("Tolerance for attack angle. Range [0, 512], 512 covers 360 degrees.")]
		public readonly new WAngle FacingTolerance = new WAngle(8);

		public override object Create(ActorInitializer init) { return new FlyVulture(init.Self, this); }
	}

	public enum OperationStateType
	{
		APPROACH,
		HARVEST,
		RETURN,
		DROP
	}

	public class OperationVulture : ISync
	{
		public WDist OperationAreaRadius;

		public List<Actor> Vultures = null;

		[Sync]
		public bool AreTargetsAcquired { get; private set; }

		[Sync]
		public WPos TargetPosition { get; private set; }

		[Sync]
		public WPos DropPosition { get; private set; }

		public World World { get; private set; }

		public WAngle AttackAngle { get; private set; }

		public string UnitType { get; private set; }

		public WPos CheckpointStart { get; private set; }
		public WPos CheckpointTarget { get; private set; }
		public WPos CheckpointDropPoint { get; private set; }

		public Player Owner { get; private set; }
		public int RevealDuration { get; private set; }

		public int LoopPeriodInTicks { get; private set; }
		public int SquadSize { get; private set; }
		public int VultureOffset { get; private set; }
		public int LoopRadius { get; private set; }

		int lastTick;
		public OperationVulture(
			WPos target,
			WPos drop,
			WDist operationAreaRadius,
			WDist cordon,
			string unitType,
			Player owner,
			int revealDuration,
			int squadSize,
			World world)
		{
			OperationAreaRadius = operationAreaRadius;
			AreTargetsAcquired = false;
			TargetPosition = target;
			DropPosition = drop;
			UnitType = unitType;
			Owner = owner;
			RevealDuration = revealDuration;
			Vultures = new List<Actor>();
			lastTick = world.WorldTick;
			SquadSize = squadSize;
			World = world;

			var altitude = World.Map.Rules.Actors[UnitType].TraitInfo<AircraftInfo>().CruiseAltitude.Length;
			WVec attackDirection = (target - drop);
			attackDirection = new WVec(attackDirection.X, attackDirection.Y, 0);
			AttackAngle = WAngle.ArcTan(attackDirection.X, attackDirection.Y);

			// Bring attackDirection to facing quantization
			attackDirection = new WVec(0, 1024, 0).Rotate(WRot.FromYaw(AttackAngle));

			CheckpointStart = target - (World.Map.DistanceToEdge(target, -attackDirection)
				+ cordon).Length * attackDirection / attackDirection.Length;

			CheckpointTarget = target + new WVec(0, 0, altitude);
			CheckpointStart += new WVec(0, 0, altitude);
			CheckpointDropPoint = drop + new WVec(0, 0, altitude);

			var rules = World.Map.Rules;
			AircraftInfo info = rules.Actors[UnitType].TraitInfo<AircraftInfo>();
			LoopPeriodInTicks = 1024 / info.TurnSpeed.Angle;
			VultureOffset = info.Speed * LoopPeriodInTicks * (SquadSize + 1) / SquadSize;
			LoopRadius = info.Speed * LoopPeriodInTicks * 100 / 628;
		}

		public void SendVultures(
			WVec squadOffset,
			Action<Actor> onEnterRange,
			Action<Actor> onExitRange,
			Action<Actor> onRemovedFromWorld)
		{
			for (var i = 0; i < SquadSize; i++)
			{
				// Includes the 90 degree rotation between body and world coordinates
				var so = squadOffset;

				var spawnOffsetShift = new WVec(0, -1024, 0).Rotate(WRot.FromYaw(AttackAngle));
				var targetOffset = new WVec(-LoopRadius, 0, 0).Rotate(WRot.FromYaw(AttackAngle));

				var vulture = World.CreateActor(false, UnitType, new TypeDictionary
				{
					new CenterPositionInit(CheckpointStart + (spawnOffsetShift * i * VultureOffset / 1024) + targetOffset),
					new OwnerInit(Owner),
					new FacingInit(AttackAngle),
				});

				Vultures.Add(vulture);
				var flyVulture = vulture.Trait<FlyVulture>();
				flyVulture.OnEnteredOperationRange += onEnterRange;
				flyVulture.OnExitedOperationRange += onExitRange;
				flyVulture.OnRemovedFromWorld += onRemovedFromWorld;
				flyVulture.Operation = this;

				World.Add(vulture);

				vulture.QueueActivity(new Fly(vulture, Target.FromPos(CheckpointTarget + targetOffset)));
				vulture.QueueActivity(new CallFunc(() => vulture.Trait<FlyVulture>().State = OperationStateType.HARVEST));
				vulture.QueueActivity(new FlyIdle(vulture, 400 - i * LoopPeriodInTicks));
				vulture.QueueActivity(new CallFunc(() => vulture.Trait<FlyVulture>().State = OperationStateType.RETURN));
				vulture.QueueActivity(new Fly(vulture, Target.FromPos(CheckpointDropPoint)));
				vulture.QueueActivity(new CallFunc(() => vulture.Trait<FlyVulture>().State = OperationStateType.DROP));
				vulture.QueueActivity(new Fly(vulture, Target.FromPos(CheckpointStart)));
				vulture.QueueActivity(new RemoveSelf());
			}
		}

		public bool IsValidTarget(Actor target)
		{
			if (Owner.RelationshipWith(target.Owner) != PlayerRelationship.Enemy)
			{
				return false;
			}

			if (target.IsDead)
			{
				return false;
			}

			if (target.TraitsImplementing<Carryable>().ToList().Count == 0)
			{
				return false;
			}

			if (target.TraitsImplementing<Mobile>().ToList().Count == 0)
			{
				return false;
			}

			if (IsDockedHarvester(target))
			{
				return false;
			}

			return true;
		}

		private bool IsDockedHarvester(Actor other)
		{
			if (other.TraitsImplementing<Harvester>().ToList().Count != 0)
				return other.Trait<WithSpriteBody>().DefaultAnimation.CurrentSequence.Name == "dock-loop";
			else
				return false;
		}

		public IEnumerable<Actor> GetUnitsInBlock(WPos blockCenterPosition, WDist squareSize, WAngle angle, int speed)
		{
			WDist diagonal = squareSize * 141 / 100;
			var candidates = World.FindActorsInCircle(blockCenterPosition, diagonal);
			WRot rotation = WRot.FromYaw(-angle);

			Func<Actor, bool> selector = (a) =>
			{
				var diff = (a.CenterPosition - blockCenterPosition).Rotate(rotation);

				bool isYInRange = (Math.Abs(diff.Y) < squareSize.Length + speed);
				bool isXInRange = (Math.Abs(diff.X) < squareSize.Length);

				return isXInRange && isXInRange;
			};
			return candidates.Where(selector);
		}

		public void CleanUp()
		{
		}
	}

	public class FlyVulture : AttackBase, ITick, ISync, INotifyRemovedFromWorld
	{
		readonly FlyVultureInfo info;

		public event Action<Actor> OnRemovedFromWorld = self => { };
		public event Action<Actor> OnEnteredOperationRange = self => { };
		public event Action<Actor> OnExitedOperationRange = self => { };

		public OperationVulture Operation;
		public OperationStateType State;

		public FlyVulture(Actor self, FlyVultureInfo info)
			: base(self, info)
		{
			this.info = info;
			State = OperationStateType.APPROACH;
		}

		void HarvestTick(Actor self)
		{
			int pickUpDistance = 1024;

			var carryallTrait = self.Trait<Carryall>();

			if (carryallTrait.Carryable != null)
				return;

			var candidatesIterable = Operation.GetUnitsInBlock(
				self.CenterPosition,
				new WDist(pickUpDistance),
				self.Trait<Aircraft>().Facing,
				self.Trait<Aircraft>().MovementSpeed);

			candidatesIterable = candidatesIterable.Where(Operation.IsValidTarget);

			List<Actor> candidates = candidatesIterable.ToList();

			if (candidates.Count == 0)
				return;

			Actor closestTarget = candidates.MaxBy(a => -(self.CenterPosition - a.CenterPosition).LengthSquared);

			CPos selfPosition = new CPos(self.CenterPosition.X, self.CenterPosition.Y);
			CPos closestTargetPosition = new CPos(closestTarget.CenterPosition.X, closestTarget.CenterPosition.Y);

			self.World.AddFrameEndTask(w =>
			{
				closestTarget.ChangeOwnerSync(self.Owner);
				if (closestTarget.TraitsImplementing<Harvester>().ToList().Count != 0)
					closestTarget.Trait<Harvester>().ChooseNewProc(closestTarget, null);
				closestTarget.CancelActivity();
				closestTarget.World.Remove(closestTarget);
				closestTarget.Trait<Carryable>().Attached(closestTarget);
				carryallTrait.AttachCarryable(self, closestTarget);
			});
		}

		void DropTick(Actor self)
		{
			Carryall carryall = self.Trait<Carryall>();
			BodyOrientation body = self.Trait<BodyOrientation>();
			Aircraft aircraft = self.Trait<Aircraft>();

			if (carryall.Carryable == null)
				return;

			var localOffset = carryall.CarryableOffset.Rotate(body.QuantizeOrientation(self, self.Orientation));
			var targetPosition = self.CenterPosition + body.LocalToWorld(localOffset);
			var targetLocation = self.World.Map.CellContaining(targetPosition);

			if (!self.World.Map.Contains(targetLocation))
				return;

			Mobile droppableMobile = carryall.Carryable.Trait<Mobile>();

			if (!droppableMobile.CanEnterCell(targetLocation))
			{
				return;
			}

			// Put back into world
			self.World.AddFrameEndTask(w =>
			{
				if (self.IsDead)
					return;

				var cargo = carryall.Carryable;
				if (cargo == null)
					return;

				carryall.Carryable.Trait<IPositionable>().SetPosition(carryall.Carryable, targetLocation, SubCell.FullCell);
				carryall.Carryable.Trait<IFacing>().Facing = facing.Facing;

				var carryable = cargo.Trait<Carryable>();
				w.Add(cargo);
				carryall.DetachCarryable(self);
				carryable.UnReserve(cargo);
				carryable.Detached(cargo);
			});
		}

		void ITick.Tick(Actor self)
		{
			switch (State)
			{
				case OperationStateType.HARVEST:
					HarvestTick(self);
					break;
				case OperationStateType.DROP:
					DropTick(self);
					break;
			}
		}

		// public void SetTarget(World w, WPos pos) { Operation.Target = Target.FromPos(pos); }
		void INotifyRemovedFromWorld.RemovedFromWorld(Actor self)
		{
			Operation.CleanUp();
			OnRemovedFromWorld(self);
		}

		public override Activity GetAttackActivity(Actor self, AttackSource source, in Target newTarget, bool allowMove, bool forceAttack, Color? targetLineColor)
		{
			throw new NotImplementedException("AttackBomber requires vulture scripted Target");
		}
	}
}
