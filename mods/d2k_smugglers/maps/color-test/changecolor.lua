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
		if not unit.IsDead then
			unit.Owner = new_owner
		end
	end)
end


FlashTime = DateTime.Seconds(8)

WorldLoaded = function()

	player = {
		Player.GetPlayer("Smugglers"),
		Player.GetPlayer("Tuek"),
		Player.GetPlayer("IX"),
		Player.GetPlayer("Ordos"),
		Player.GetPlayer("Mercenaries"),
		Player.GetPlayer("Atreides"),
		Player.GetPlayer("Harkonnen"),
		Player.GetPlayer("Fremen"),
		Player.GetPlayer("Corrino"),
	} 

	all_units = player[1].GetActors()
	neutral_units = Player.GetPlayer("Neutral").GetActors()
	ChangeOwner(neutral_units, player[1])

	ChangeColor = function()
		i = i % 9 + 1
		ChangeOwner(all_units, player[i])
		Trigger.AfterDelay(FlashTime, ChangeColor)
	end


	Trigger.AfterDelay(FlashTime, ChangeColor)


end