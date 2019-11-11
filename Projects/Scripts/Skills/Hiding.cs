using System;
using System.Linq;
using Server.Multis;
using Server.Network;
using Server.Targeting;

namespace Server.SkillHandlers
{
  public class Hiding
  {
    public static bool CombatOverride{ get; set; }

    public static void Initialize()
    {
      SkillInfo.Table[21].Callback = OnUse;
    }

    public static TimeSpan OnUse(Mobile m)
    {
      if (m.Spell != null)
      {
        m.SendLocalizedMessage(501238); // You are busy doing something else and cannot hide.
        return TimeSpan.FromSeconds(1.0);
      }

      if (Core.ML && m.Target != null) Target.Cancel(m);

      double bonus = 0.0;

      BaseHouse house = BaseHouse.FindHouseAt(m);

      if (house?.IsFriend(m) == true)
        bonus = 100.0;
      else if (!Core.AOS)
      {
        house ??= BaseHouse.FindHouseAt(new Point3D(m.X - 1, m.Y, 127), m.Map, 16) ??
                  BaseHouse.FindHouseAt(new Point3D(m.X + 1, m.Y, 127), m.Map, 16) ??
                  BaseHouse.FindHouseAt(new Point3D(m.X, m.Y - 1, 127), m.Map, 16) ??
                  BaseHouse.FindHouseAt(new Point3D(m.X, m.Y + 1, 127), m.Map, 16);

        if (house != null)
          bonus = 50.0;
      }

      //int range = 18 - (int)(m.Skills.Hiding.Value / 10);
      int range = Math.Min((int)((100 - m.Skills.Hiding.Value) / 2) + 8,
        18); //Cap of 18 not OSI-exact, intentional difference

      bool badCombat = !CombatOverride && m.Combatant != null && m.InRange(m.Combatant.Location, range) &&
                       m.Combatant.InLOS(m);
      bool ok = !badCombat;

      if (ok)
      {
        if (!CombatOverride)
          if (m.GetMobilesInRange(range).Any(check => check.InLOS(m) && check.Combatant == m))
            badCombat = true;

        ok = !badCombat && m.CheckSkill(SkillName.Hiding, 0.0 - bonus, 100.0 - bonus);
      }

      if (badCombat)
      {
        m.RevealingAction();

        m.LocalOverheadMessage(MessageType.Regular, 0x22, 501237); // You can't seem to hide right now.

        return TimeSpan.FromSeconds(1.0);
      }

      if (ok)
      {
        m.Hidden = true;
        m.Warmode = false;
        m.LocalOverheadMessage(MessageType.Regular, 0x1F4, 501240); // You have hidden yourself well.
      }
      else
      {
        m.RevealingAction();
        m.LocalOverheadMessage(MessageType.Regular, 0x22, 501241); // You can't seem to hide here.
      }

      return TimeSpan.FromSeconds(10.0);
    }
  }
}