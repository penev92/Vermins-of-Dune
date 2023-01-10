--[[
   Copyright 2007-2020 The OpenRA Developers (see AUTHORS)
   This file is part of OpenRA, which is free software. It is made
   available to you under the terms of the GNU General Public License
   as published by the Free Software Foundation, either version 3 of
   the License, or (at your option) any later version. For more
   information, see COPYING.
]]


players = {}

i = 0

ChangeOwner = function(units, new_owner)
	Utils.Do(units, function(unit)
		if not unit.IsDead and unit.IsInWorld then
			unit.Owner = new_owner
		end
	end)
end


FlashTime = DateTime.Seconds(8)

all_unit_types = {
	"wind_trap",
	"refinery",
	"barracks",
	"light_factory",
	"heavy_factory",
	-- "silo",
	"construction_yard",
	"outpost",
	"high_tech_factory",
	"starport",
	"palace",

	"quad",
	"trike",
	"raider",
	"stealth_raider",
	"shuttle",
	
	"combat_tank_s",
	"combat_tank_o",
	"combat_tank_h",
	"combat_tank_a",
	"siege_tank",
	"missile_tank",
	"civilian",
	"nsfremen",
	"engineer",
	"scavanger",
}

WorldLoaded = function()

	-- player = {
	-- 	Player.GetPlayer("Smugglers"),
	-- 	Player.GetPlayer("IX"),
	-- 	Player.GetPlayer("Ordos"),
	-- 	Player.GetPlayer("Mercenaries"),
	-- 	Player.GetPlayer("Atreides"),
	-- 	Player.GetPlayer("Harkonnen"),
	-- 	Player.GetPlayer("Fremen"),
	-- 	Player.GetPlayer("Corrino"),
	-- } 
	
	-- colored_units = player[1].GetActorsByTypes(all_unit_types)
	-- neutral_units = Player.GetPlayer("Neutral").GetActorsByTypes(all_unit_types)
	-- ChangeOwner(neutral_units, player[1])

	-- ChangeColor = function()
	-- 	i = i % 8 + 1
	-- 	ChangeOwner(colored_units, player[i])
	-- 	Trigger.AfterDelay(FlashTime, ChangeColor)
	-- end

	-- Trigger.AfterDelay(FlashTime, ChangeColor)

end
