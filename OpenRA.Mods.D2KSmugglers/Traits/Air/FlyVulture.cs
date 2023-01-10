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
		public List<Actor> PossibleVictims = null;
		public Dictionary<LoopingSpriteEffect, Actor> FlyEffects = null;
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

		public Player Owner { get; private set; }
		public int RevealDuration { get; private set; }

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

			AttackAngle = WAngle.ArcTan(-attackDirection.X, attackDirection.Y);
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
				vulture.QueueActivity(new Fly(vulture, Target.FromPos(CheckpointStart + spawnOffset)));
				vulture.QueueActivity(new RemoveSelf());
			}

			Dispetcher.NotifyVulturesArrived(Vultures);
		}

		public void RevealTarget()
		{
			World.AddFrameEndTask(w =>
			{
				var revealShroudEffect = new RevealShroudEffect(
					new WPos(CheckpointTarget.X, CheckpointTarget.Y, 0),
					new WDist(8192),
					Shroud.SourceType.Visibility,
					Owner,
					PlayerRelationship.Ally,
					0,
					RevealDuration);
				w.Add(revealShroudEffect);
			});
		}

		public bool IsValidTarget(Actor target)
		{
			if (Owner.RelationshipWith(target.Owner) == PlayerRelationship.Enemy)
			{
				return true;
			}

			return false;
		}

		public void ReleaseFlies()
		{
			if (AreTargetsAcquired)
			{
				throw new Exception("ReleaseFlies cannot be called twice");
			}

			var targets = World.FindActorsInCircle(TargetPosition, OperationAreaRadius);
			PossibleVictims = targets.ToList();
			FlyEffects = new Dictionary<LoopingSpriteEffect, Actor>();

			foreach (Actor actor in PossibleVictims)
			{
				if (!IsValidTarget(actor))
					continue;
				Actor closestVulture = Vultures.MaxBy(v => -(actor.CenterPosition - v.CenterPosition).LengthSquared);

				FlyParticle fly = new FlyParticle();
				fly.Position = closestVulture.CenterPosition;
				fly.Speed = new WVec(0, closestVulture.Trait<Aircraft>().MovementSpeed, 0).Rotate(
					WRot.FromYaw(closestVulture.TraitsImplementing<Aircraft>().First().Facing)) / 2;

				Func<WPos> positionFunc = () =>
				{
					WVec randomIncrement = new WVec(World.SharedRandom.Next() % 30 - 15,
													World.SharedRandom.Next() % 30 - 15,
													World.SharedRandom.Next() % 30 - 15);
					WPos goalPosition = new WPos(actor.CenterPosition.X, actor.CenterPosition.Y, 512);
					WVec speedGoal = (goalPosition - fly.Position) / 10;
					fly.Speed = (80 * fly.Speed + 20 * speedGoal) / 100 + randomIncrement;
					fly.Speed = new WVec(fly.Speed.X, fly.Speed.Y, fly.Speed.Z);
					fly.Position = fly.Position + fly.Speed;
					return fly.Position;
				};

				var flyEffect = new LoopingSpriteEffect(positionFunc, () => new WAngle(0), World, "vulture-effect", "fly", "effect");
				World.AddFrameEndTask(w => w.Add(flyEffect));
				FlyEffects.Add(flyEffect, actor);
			}

			Dispetcher.NotifyReleaseFlies();
			AreTargetsAcquired = true;
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

			PossibleVictims = null;
		}
	}

	public class FlyVulture : AttackBase, ITick, ISync, INotifyRemovedFromWorld
	{
		readonly FlyVultureInfo info;

		[Sync]
		bool inOperationArea;

		public event Action<Actor> OnRemovedFromWorld = self => { };
		public event Action<Actor> OnEnteredOperationRange = self => { };
		public event Action<Actor> OnExitedOperationRange = self => { };

		public OperationVulture Operation;

		public FlyVulture(Actor self, FlyVultureInfo info)
			: base(self, info)
		{
			this.info = info;
		}

		void ITick.Tick(Actor self)
		{
			var wasInOperationArea = inOperationArea;
			inOperationArea = false;

			if (self.IsInWorld)
			{
				var deltePosition = Operation.TargetPosition - self.CenterPosition;
				WDist distance = new WDist(new WVec(deltePosition.X, deltePosition.Y, 0).Length);

				inOperationArea = distance < Operation.OperationAreaRadius;
			}

			if (inOperationArea && !wasInOperationArea)
			{
				OnEnteredOperationRange(self);
				if (!Operation.AreTargetsAcquired)
				{
					Operation.RevealTarget();
					Operation.ReleaseFlies();
				}
			}

			if (!inOperationArea && wasInOperationArea)
			{
				// Remove all units after 2nd pass
				if (Operation.Dispetcher.GetStage() == OperationVultureStage.RETURN)
				{
					Operation.CleanUp();
				}
				else
				{
				}

				OnExitedOperationRange(self);
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
			throw new NotImplementedException("AttackBomber requires vulture scripted target");
		}
	}
}
