--[[
   Copyright 2007-2020 The OpenRA Developers (see AUTHORS)
   This file is part of OpenRA, which is free software. It is made
   available to you under the terms of the GNU General Public License
   as published by the Free Software Foundation, either version 3 of
   the License, or (at your option) any later version. For more
   information, see COPYING.
]]


ToHarvest =
{
	easy = 2500,
	normal = 3000,
	hard = 3500,
	impossible = 4000
}

FremenAttackCooldown =
{
	easy = 100,
	normal = 80,
	hard = 60,
	impossible = 40
}


Messages =
{
	"Build a concrete foundation before placing your buildings.",
	"Build a Wind Trap for power.",
	"Build a Refinery to collect Spice.",
	"Build a Silo to store additional Spice."
}

StarterUnits = {"mcv", "light_inf", "light_inf", "raider", "thumper"}
SmugglerReinforcements = {"thumper", "light_inf", "light_inf", "light_inf"}

-- PickPlayerStructure = function(player)
-- end 

-- AttackEnemyBase = function(unit, player_enemy)
-- 	unit.AttackMove(SmugglerRF1Drop.Location)
-- end

Tick = function()
	-- if FremenArrived and fremen.HasNoRequiredUnits() then
	-- 	player.MarkCompletedObjective(KillHarkonnen)
	-- end

	if player.Resources > SpiceToHarvest - 1 then
		player.MarkCompletedObjective(GatherSpice)
	end

	-- player has no Wind Trap
	if (player.PowerProvided <= 20 or player.PowerState ~= "Normal") and DateTime.GameTime % DateTime.Seconds(32) == 0 then
		HasPower = false
		Media.DisplayMessage(Messages[2], SmugglerSpeaker)
	else
		HasPower = true
	end

	-- player has no Refinery and no Silos
	if HasPower and player.ResourceCapacity == 0 and DateTime.GameTime % DateTime.Seconds(32) == 0 then
		Media.DisplayMessage(Messages[3], SmugglerSpeaker)
	end

	if HasPower and player.Resources > player.ResourceCapacity * 0.8 and DateTime.GameTime % DateTime.Seconds(32) == 0 then
		Media.DisplayMessage(Messages[4], SmugglerSpeaker)
	end

	UserInterface.SetMissionText("Harvested resources: " .. player.Resources .. "/" .. SpiceToHarvest, player.Color)
end

WorldLoaded = function()
	player = Player.GetPlayer("Smugglers")
	fremen = Player.GetPlayer("Fremen")

	SpiceToHarvest = ToHarvest[Difficulty]
	InitObjectives(player)
	GatherSpice = player.AddPrimaryObjective("Harvest " .. tostring(SpiceToHarvest) .. " Solaris worth of Spice.")
	-- KillHarkonnen = player.AddSecondaryObjective("Eliminate all Harkonnen units and reinforcements\nin the area.")

	local checkResourceCapacity = function()
		Trigger.AfterDelay(0, function()
			if player.ResourceCapacity < SpiceToHarvest then
				Media.DisplayMessage("We don't have enough silo space to store the required amount of Spice!", SmugglerSpeaker)
				Trigger.AfterDelay(DateTime.Seconds(3), function()
					fremen.MarkCompletedObjective(KillSmugglers)
				end)

				return true
			end
		end)
	end

	Reinforcements.ReinforceWithTransport(player, "carryall.reinforce", StarterUnits,
	                                      {SmugglerRF1Entry.Location, SmugglerRF1Drop.Location}, {SmugglerRF1Exit.Location})

	Trigger.AfterDelay(DateTime.Seconds(150), function() 
	    Reinforcements.ReinforceWithTransport(player, "carryall.reinforce", SmugglerReinforcements,
		{SmugglerRF2Entry.Location, SmugglerRF2Drop.Location}, {SmugglerRF2Exit.Location})
	end)

	Media.DisplayMessage(Messages[1], "Mentat")
	Media.StopMusic()
	Media.PlayMusic("desoper")
	CurrentWave = 1
	RunWaves()
end

RunWaves = function()
	local units = {"nsfremen", "nsfremen"}

	if CurrentWave == 1 then
		delay = DateTime.Seconds(90)
	else
		delay = DateTime.Seconds(FremenAttackCooldown[Difficulty])
	end

	Trigger.AfterDelay(delay, function()
		ReinforcementLocation = Utils.Random({FremenReinforceEntry1.Location, FremenReinforceEntry2.Location}) 
		reinforcements = Reinforcements.Reinforce(fremen, units, {ReinforcementLocation}, 10, IdleHunt)
		Utils.Do(reinforcements, function(unit)
			-- unit.AttackMove(SmugglerRF1Drop.Location)
			IdleHunt(unit)
		end)
		CurrentWave = CurrentWave + 1
		RunWaves()
	end)
end


