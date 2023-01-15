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
using OpenRA.Mods.D2KSmugglers.Traits.Air;
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

	public class FlyParticle
	{
		public FlyParticle() { }
		public FlyParticle(WPos pos, WVec speed)
		{
			Position = pos;
			Speed = speed;
		}

		public WPos Position;
		public WVec Speed;
	}

	public class OperationVulture : ISync
	{
		public WDist OperationAreaRadius;

		public OperationVultureDispetcher Dispetcher;
		public HashSet<Actor> PossibleVictims = null;
		public Dictionary<LoopingSpriteEffect, Actor> FlyEffects = null;
		public List<AttachedRevealShroudEffect> RevealEffects = null;
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
		public WPos CheckpointFinish { get; private set; }
		public WPos CheckpointTurning1 { get; private set; }
		public WPos CheckpointTurning2 { get; private set; }
		public WPos CheckpointTurning3 { get; private set; }
		public WPos CheckpointDropPoint { get; private set; }

		public Player Owner { get; private set; }
		public int RevealDuration { get; private set; }

		int lastTick;
		public OperationVulture(
			WPos target,
			WPos drop,
			WDist operationAreaRadius,
			WDist cordon,
			string unitType,
			Player owner,
			int revealDuration,
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
			FlyEffects = new Dictionary<LoopingSpriteEffect, Actor>();
			Dispetcher = new OperationVultureDispetcher();
			RevealEffects = new List<AttachedRevealShroudEffect>();
			lastTick = world.WorldTick;

			World = world;

			var altitude = World.Map.Rules.Actors[UnitType].TraitInfo<AircraftInfo>().CruiseAltitude.Length;
			WVec attackDirection = (target - drop);
			attackDirection = new WVec(attackDirection.X, attackDirection.Y, 0);
			attackDirection = attackDirection * 1024 / attackDirection.Length;

			CheckpointStart = target - (World.Map.DistanceToEdge(target, -attackDirection)
				+ cordon).Length * attackDirection / attackDirection.Length;
			CheckpointFinish = target + (World.Map.DistanceToEdge(target, attackDirection)
				+ cordon).Length * attackDirection / attackDirection.Length;

			CheckpointTarget = target + new WVec(0, 0, altitude);
			CheckpointStart += new WVec(0, 0, altitude);
			CheckpointFinish += new WVec(0, 0, altitude);

			CheckpointTurning1 = CheckpointFinish + new WVec(0, 0, altitude) + attackDirection * 20;
			CheckpointTurning2 = CheckpointFinish + new WVec(0, 0, altitude) + attackDirection * 40;
			CheckpointTurning3 = CheckpointFinish + new WVec(0, 0, altitude) + attackDirection * 60;
			CheckpointDropPoint = drop + new WVec(0, 0, altitude);

			AttackAngle = WAngle.ArcTan(-attackDirection.X, attackDirection.Y);

			PossibleVictims = new HashSet<Actor>();
			FlyEffects = new Dictionary<LoopingSpriteEffect, Actor>();
		}

		public void SendVultures(
			int squadSize,
			WVec squadOffset,
			Action<Actor> onEnterRange,
			Action<Actor> onExitRange,
			Action<Actor> onRemovedFromWorld)
		{
			// Create the actors immediately so they can be returned
			for (var i = -squadSize / 2; i <= squadSize / 2; i++)
			{
				// Even-sized squads skip the lead plane
				if (i == 0 && (squadSize & 1) == 0)
					continue;

				// Includes the 90 degree rotation between body and world coordinates
				var so = squadOffset;
				var spawnOffset = new WVec(i * so.Y, -Math.Abs(i) * so.X, 0).Rotate(WRot.FromYaw(AttackAngle));

				var vulture = World.CreateActor(false, UnitType, new TypeDictionary
				{
					new CenterPositionInit(CheckpointStart + spawnOffset),
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

				vulture.QueueActivity(new Fly(vulture, Target.FromPos(CheckpointTarget + spawnOffset)));
				vulture.QueueActivity(new Fly(vulture, Target.FromPos(CheckpointFinish + spawnOffset)));
				vulture.QueueActivity(new Fly(vulture, Target.FromPos(CheckpointTurning3 + spawnOffset)));
				vulture.QueueActivity(new Fly(vulture, Target.FromPos(CheckpointTurning2 + spawnOffset)));
				vulture.QueueActivity(new Fly(vulture, Target.FromPos(CheckpointTurning1 + spawnOffset)));
				vulture.QueueActivity(new Fly(vulture, Target.FromPos(CheckpointFinish + spawnOffset)));
				vulture.QueueActivity(new CallFunc(() => Dispetcher.NotifyFinishScoutRun(vulture)));

				vulture.QueueActivity(new Fly(vulture, Target.FromPos(CheckpointTarget + spawnOffset)));
				vulture.QueueActivity(new Fly(vulture, Target.FromPos(CheckpointDropPoint + spawnOffset)));
				vulture.QueueActivity(new CallFunc(() => Dispetcher.NotifyFinishHarvestRun(vulture)));

				vulture.QueueActivity(new Fly(vulture, Target.FromPos(CheckpointStart + spawnOffset)));
				vulture.QueueActivity(new RemoveSelf());
			}

			Dispetcher.NotifyVulturesArrived(Vultures);
		}

		public void TryRunTick()
		{
			// Run only once per tick
			if (World.WorldTick == lastTick)
			{
				return;
			}

			foreach (Actor actor in PossibleVictims.ToList())
			{
				if (!IsValidTarget(actor))
					RemoveTarget(actor);
			}

			lastTick = World.WorldTick;
		}

		public void RemoveTarget(Actor target)
		{
			PossibleVictims.Remove(target);

			foreach (LoopingSpriteEffect effect in FlyEffects.Keys.ToList())
			{
				if (FlyEffects[effect] == target)
				{
					FlyEffects.Remove(effect);
					effect.Terminate();
				}
			}

			foreach (AttachedRevealShroudEffect effect in RevealEffects.ToList())
			{
				if (effect.Target.Actor == target)
				{
					RevealEffects.Remove(effect);
					effect.Terminate(World);
				}
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
			if (FlyEffects != null)
			{
				foreach (LoopingSpriteEffect effect in FlyEffects.Keys)
				{
					effect.Terminate();
				}

				FlyEffects = null;
			}

			if (RevealEffects != null)
			{
				foreach (var effect in RevealEffects)
				{
					effect.Terminate(World);
				}

				RevealEffects = null;
			}

			PossibleVictims = null;
		}
	}

	public class FlyVulture : AttackBase, ITick, ISync, INotifyRemovedFromWorld
	{
		readonly FlyVultureInfo info;

		public event Action<Actor> OnRemovedFromWorld = self => { };
		public event Action<Actor> OnEnteredOperationRange = self => { };
		public event Action<Actor> OnExitedOperationRange = self => { };

		public OperationVulture Operation;

		public FlyVulture(Actor self, FlyVultureInfo info)
			: base(self, info)
		{
			this.info = info;
		}

		void ScoutTick(Actor self)
		{
			var candidates = Operation.GetUnitsInBlock(
				self.CenterPosition,
				Operation.OperationAreaRadius,
				self.Trait<Aircraft>().Facing,
				self.Trait<Aircraft>().MovementSpeed);

			candidates = candidates.Where(c => !Operation.PossibleVictims.Contains(c));

			foreach (Actor actor in candidates)
			{
				if (!Operation.IsValidTarget(actor))
					continue;

				Operation.PossibleVictims.Add(actor);

				FlyParticle fly = new FlyParticle();
				fly.Position = self.CenterPosition;
				fly.Speed = new WVec(0, self.Trait<Aircraft>().MovementSpeed, 0).Rotate(
					WRot.FromYaw(self.TraitsImplementing<Aircraft>().First().Facing)) / 2;

				Func<WPos> positionFunc = () =>
				{
					WVec randomIncrement = new WVec(self.World.SharedRandom.Next() % 30 - 15,
													self.World.SharedRandom.Next() % 30 - 15,
													self.World.SharedRandom.Next() % 30 - 15);
					WPos goalPosition = new WPos(actor.CenterPosition.X, actor.CenterPosition.Y, 512);
					WVec speedGoal = (goalPosition - fly.Position) / 10;
					fly.Speed = (80 * fly.Speed + 20 * speedGoal) / 100 + randomIncrement;
					fly.Speed = new WVec(fly.Speed.X, fly.Speed.Y, fly.Speed.Z);
					fly.Position = fly.Position + fly.Speed;
					return fly.Position;
				};

				var flyEffect = new LoopingSpriteEffect(
					positionFunc,
					() => new WAngle(0),
					self.World,
					"vulture-effect",
					"fly", "effect");

				var revealShroudEffect = new AttachedRevealShroudEffect(
						Target.FromActor(actor),
						WDist.FromCells(2),
						Shroud.SourceType.Visibility,
						self.Owner,
						PlayerRelationship.Ally,
						0,
						100000);

				Operation.FlyEffects.Add(flyEffect, actor);
				Operation.RevealEffects.Add(revealShroudEffect);

				self.World.AddFrameEndTask(w =>
				{
					w.Add(flyEffect);
					w.Add(revealShroudEffect);
				});
			}
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
			candidatesIterable = candidatesIterable.Where(a => Operation.PossibleVictims.Contains(a));

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

			carryall.Carryable.Trait<IPositionable>().SetPosition(carryall.Carryable, targetLocation, SubCell.FullCell);
			carryall.Carryable.Trait<IFacing>().Facing = facing.Facing;

			// Put back into world
			self.World.AddFrameEndTask(w =>
			{
				if (self.IsDead)
					return;

				var cargo = carryall.Carryable;
				if (cargo == null)
					return;

				var carryable = cargo.Trait<Carryable>();
				w.Add(cargo);
				carryall.DetachCarryable(self);
				carryable.UnReserve(cargo);
				carryable.Detached(cargo);
			});
		}

		void ITick.Tick(Actor self)
		{
			switch (Operation.Dispetcher.GetStage())
			{
				case OperationVultureStage.SCOUT:
					ScoutTick(self);
					break;
				case OperationVultureStage.HARVEST:
					HarvestTick(self);
					break;
				case OperationVultureStage.DROP:
					DropTick(self);
					break;
			}

			Operation.TryRunTick();
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
