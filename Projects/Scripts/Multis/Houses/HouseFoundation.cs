using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Server.Gumps;
using Server.Items;
using Server.Mobiles;
using Server.Network;
using Server.Spells;

namespace Server.Multis
{
  public enum FoundationType
  {
    Stone,
    DarkWood,
    LightWood,
    Dungeon,
    Brick,
    ElvenGrey,
    ElvenNatural,
    Crystal,
    Shadow
  }

  public class HouseFoundation : BaseHouse
  {
    private static ComponentVerification m_Verification;

    public static readonly bool AllowStairSectioning = true;

    /* Stair block IDs
     * (sorted ascending)
     */
    private static int[] m_BlockIDs =
    {
      0x3EE, 0x709, 0x71E, 0x721,
      0x738, 0x750, 0x76C, 0x788,
      0x7A3, 0x7BA, 0x35D2, 0x3609,
      0x4317, 0x4318, 0x4B07, 0x7807
    };

    /* Stair sequence IDs
     * (sorted ascending)
     * Use this for stairs in the proper N,W,S,E sequence
     */
    private static int[] m_StairSeqs =
    {
      0x3EF, 0x70A, 0x722, 0x739,
      0x751, 0x76D, 0x789, 0x7A4
    };

    /* Other stair IDs
     * Listed in order: north, west, south, east
     * Use this for stairs not in the proper sequence
     */
    private static int[] m_StairIDs =
    {
      0x71F, 0x736, 0x737, 0x749,
      0x35D4, 0x35D3, 0x35D6, 0x35D5,
      0x360B, 0x360A, 0x360D, 0x360C,
      0x4360, 0x435E, 0x435F, 0x4361,
      0x435C, 0x435A, 0x435B, 0x435D,
      0x4364, 0x4362, 0x4363, 0x4365,
      0x4B05, 0x4B04, 0x4B34, 0x4B33,
      0x7809, 0x7808, 0x780A, 0x780B,
      0x7BB, 0x7BC
    };

    private DesignState m_Backup; // State at last user backup.
    private DesignState m_Current; // State which is currently visible.

    private int m_DefaultPrice;
    private DesignState m_Design; // State of current design.

    public HouseFoundation(Mobile owner, int multiID, int maxLockdowns, int maxSecures)
      : base(multiID, owner, maxLockdowns, maxSecures)
    {
      SignpostGraphic = 9;

      Fixtures = new List<Item>();

      int x = Components.Min.X;
      int y = Components.Height - 1 - Components.Center.Y;

      SignHanger = new Static(0xB98);
      SignHanger.MoveToWorld(new Point3D(X + x, Y + y, Z + 7), Map);

      CheckSignpost();

      SetSign(x, y, 7);
    }

    public HouseFoundation(Serial serial)
      : base(serial)
    {
    }

    public FoundationType Type{ get; set; }

    public int LastRevision{ get; set; }

    public List<Item> Fixtures{ get; private set; }

    public Item SignHanger{ get; private set; }

    public Item Signpost{ get; private set; }

    public int SignpostGraphic{ get; set; }

    public Mobile Customizer{ get; set; }

    public override bool IsAosRules => true;

    public override bool IsActive => Customizer == null;

    public virtual int CustomizationCost => Core.AOS ? 0 : 10000;

    public override MultiComponentList Components
    {
      get
      {
        if (m_Current == null) SetInitialState();
        return m_Current.Components;
      }
    }

    public DesignState CurrentState
    {
      get
      {
        if (m_Current == null) SetInitialState();
        return m_Current;
      }
      set => m_Current = value;
    }

    public DesignState DesignState
    {
      get
      {
        if (m_Design == null) SetInitialState();
        return m_Design;
      }
      set => m_Design = value;
    }

    public DesignState BackupState
    {
      get
      {
        if (m_Backup == null) SetInitialState();
        return m_Backup;
      }
      set => m_Backup = value;
    }

    public override Rectangle2D[] Area
    {
      get
      {
        MultiComponentList mcl = Components;

        return new[] { new Rectangle2D(mcl.Min.X, mcl.Min.Y, mcl.Width, mcl.Height) };
      }
    }

    public override Point3D BaseBanLocation =>
      new Point3D(Components.Min.X, Components.Height - 1 - Components.Center.Y, 0);

    public override int DefaultPrice => m_DefaultPrice;

    public int MaxLevels
    {
      get
      {
        MultiComponentList mcl = Components;

        if (mcl.Width >= 14 || mcl.Height >= 14)
          return 4;
        return 3;
      }
    }

    public static ComponentVerification Verification => m_Verification ??= new ComponentVerification();

    public bool IsFixture(Item item) => Fixtures.Contains(item);

    public override int GetMaxUpdateRange() => 24;

    public override int GetUpdateRange(Mobile m)
    {
      int w = CurrentState.Components.Width;
      int h = CurrentState.Components.Height - 1;
      int v = 18 + (w > h ? w : h) / 2;

      if (v > 24)
        v = 24;
      else if (v < 18)
        v = 18;

      return v;
    }

    public void SetInitialState()
    {
      // This is a new house, it has not yet loaded a design state
      m_Current = new DesignState(this, GetEmptyFoundation());
      m_Design = new DesignState(m_Current);
      m_Backup = new DesignState(m_Current);
    }

    public override void OnAfterDelete()
    {
      base.OnAfterDelete();

      SignHanger?.Delete();

      Signpost?.Delete();

      if (Fixtures == null)
        return;

      for (int i = 0; i < Fixtures.Count; ++i)
      {
        Item item = Fixtures[i];

        item?.Delete();
      }

      Fixtures.Clear();
    }

    public override void OnLocationChange(Point3D oldLocation)
    {
      base.OnLocationChange(oldLocation);

      int x = Location.X - oldLocation.X;
      int y = Location.Y - oldLocation.Y;
      int z = Location.Z - oldLocation.Z;

      SignHanger?.MoveToWorld(new Point3D(SignHanger.X + x, SignHanger.Y + y, SignHanger.Z + z), Map);

      Signpost?.MoveToWorld(new Point3D(Signpost.X + x, Signpost.Y + y, Signpost.Z + z), Map);

      if (Fixtures == null)
        return;

      for (int i = 0; i < Fixtures.Count; ++i)
      {
        Item item = Fixtures[i];

        if (item is BaseDoor door && Doors.Contains(door))
          continue;

        item.MoveToWorld(new Point3D(item.X + x, item.Y + y, item.Z + z), Map);
      }
    }

    public override void OnMapChange()
    {
      base.OnMapChange();

      if (SignHanger != null)
        SignHanger.Map = Map;

      if (Signpost != null)
        Signpost.Map = Map;

      if (Fixtures == null)
        return;

      for (int i = 0; i < Fixtures.Count; ++i)
        Fixtures[i].Map = Map;
    }

    public void ClearFixtures(Mobile from)
    {
      if (Fixtures == null)
        return;

      RemoveKeys(from);

      for (int i = 0; i < Fixtures.Count; ++i)
      {
        Item item = Fixtures[i];
        item.Delete();

        if (item is BaseDoor door)
          Doors.Remove(door);
      }

      Fixtures.Clear();
    }

    public void AddFixtures(Mobile from, MultiTileEntry[] list)
    {
      Fixtures ??= new List<Item>();

      uint keyValue = 0;

      for (int i = 0; i < list.Length; ++i)
      {
        MultiTileEntry mte = list[i];
        int itemID = mte.m_ItemID;

        if (itemID >= 0x181D && itemID < 0x1829)
        {
          HouseTeleporter tp = new HouseTeleporter(itemID);

          AddFixture(tp, mte);
        }
        else
        {
          BaseDoor door = null;

          if (itemID >= 0x675 && itemID < 0x6F5)
          {
            int type = (itemID - 0x675) / 16;
            DoorFacing facing = (DoorFacing)((itemID - 0x675) / 2 % 8);

            door = type switch
            {
              0 => new GenericHouseDoor(facing, 0x675, 0xEC, 0xF3),
              1 => new GenericHouseDoor(facing, 0x685, 0xEC, 0xF3),
              2 => new GenericHouseDoor(facing, 0x695, 0xEB, 0xF2),
              3 => new GenericHouseDoor(facing, 0x6A5, 0xEA, 0xF1),
              4 => new GenericHouseDoor(facing, 0x6B5, 0xEA, 0xF1),
              5 => new GenericHouseDoor(facing, 0x6C5, 0xEC, 0xF3),
              6 => new GenericHouseDoor(facing, 0x6D5, 0xEA, 0xF1),
              7 => new GenericHouseDoor(facing, 0x6E5, 0xEA, 0xF1),
              _ => door
            };
          }
          else if (itemID >= 0x314 && itemID < 0x364)
          {
            int type = (itemID - 0x314) / 16;
            DoorFacing facing = (DoorFacing)((itemID - 0x314) / 2 % 8);
            door = new GenericHouseDoor(facing, 0x314 + type * 16, 0xED, 0xF4);
          }
          else if (itemID >= 0x824 && itemID < 0x834)
          {
            DoorFacing facing = (DoorFacing)((itemID - 0x824) / 2 % 8);
            door = new GenericHouseDoor(facing, 0x824, 0xEC, 0xF3);
          }
          else if (itemID >= 0x839 && itemID < 0x849)
          {
            DoorFacing facing = (DoorFacing)((itemID - 0x839) / 2 % 8);
            door = new GenericHouseDoor(facing, 0x839, 0xEB, 0xF2);
          }
          else if (itemID >= 0x84C && itemID < 0x85C)
          {
            DoorFacing facing = (DoorFacing)((itemID - 0x84C) / 2 % 8);
            door = new GenericHouseDoor(facing, 0x84C, 0xEC, 0xF3);
          }
          else if (itemID >= 0x866 && itemID < 0x876)
          {
            DoorFacing facing = (DoorFacing)((itemID - 0x866) / 2 % 8);
            door = new GenericHouseDoor(facing, 0x866, 0xEB, 0xF2);
          }
          else if (itemID >= 0xE8 && itemID < 0xF8)
          {
            DoorFacing facing = (DoorFacing)((itemID - 0xE8) / 2 % 8);
            door = new GenericHouseDoor(facing, 0xE8, 0xED, 0xF4);
          }
          else if (itemID >= 0x1FED && itemID < 0x1FFD)
          {
            DoorFacing facing = (DoorFacing)((itemID - 0x1FED) / 2 % 8);
            door = new GenericHouseDoor(facing, 0x1FED, 0xEC, 0xF3);
          }
          else if (itemID >= 0x241F && itemID < 0x2421)
          {
            //DoorFacing facing = (DoorFacing)(((itemID - 0x241F) / 2) % 8);
            door = new GenericHouseDoor(DoorFacing.NorthCCW, 0x2415, -1, -1);
          }
          else if (itemID >= 0x2423 && itemID < 0x2425)
          {
            //DoorFacing facing = (DoorFacing)(((itemID - 0x241F) / 2) % 8);
            //This one and the above one are 'special' cases, ie: OSI had the ItemID pattern discombobulated for these
            door = new GenericHouseDoor(DoorFacing.WestCW, 0x2423, -1, -1);
          }
          else if (itemID >= 0x2A05 && itemID < 0x2A1D)
          {
            DoorFacing facing = (DoorFacing)((itemID - 0x2A05) / 2 % 4 + 8);

            int sound = itemID >= 0x2A0D && itemID < 0x2a15 ? 0x539 : -1;

            door = new GenericHouseDoor(facing, 0x29F5 + 8 * ((itemID - 0x2A05) / 8), sound, sound);
          }
          else if (itemID == 0x2D46)
            door = new GenericHouseDoor(DoorFacing.NorthCW, 0x2D46, 0xEA, 0xF1, false);
          else if (itemID == 0x2D48 || itemID == 0x2FE2)
            door = new GenericHouseDoor(DoorFacing.SouthCCW, itemID, 0xEA, 0xF1, false);
          else if (itemID >= 0x2D63 && itemID < 0x2D70)
          {
            int mod = (itemID - 0x2D63) / 2 % 2;
            DoorFacing facing = mod == 0 ? DoorFacing.SouthCCW : DoorFacing.WestCCW;

            int type = (itemID - 0x2D63) / 4;

            door = new GenericHouseDoor(facing, 0x2D63 + 4 * type + mod * 2, 0xEA, 0xF1, false);
          }
          else if (itemID == 0x2FE4 || itemID == 0x31AE)
            door = new GenericHouseDoor(DoorFacing.WestCCW, itemID, 0xEA, 0xF1, false);
          else if (itemID >= 0x319C && itemID < 0x31AE)
          {
            //special case for 0x31aa <-> 0x31a8 (a9)

            int mod = (itemID - 0x319C) / 2 % 2;

            bool specialCase = itemID == 0x31AA || itemID == 0x31A8;

            DoorFacing facing;

            if (itemID == 0x31AA || itemID == 0x31A8)
              facing = mod == 0 ? DoorFacing.NorthCW : DoorFacing.EastCW;
            else
              facing = mod == 0 ? DoorFacing.EastCW : DoorFacing.NorthCW;

            int type = (itemID - 0x319C) / 4;

            door = new GenericHouseDoor(facing, 0x319C + 4 * type + mod * 2, 0xEA, 0xF1, false);
          }
          else if (itemID >= 0x367B && itemID < 0x369B)
          {
            int type = (itemID - 0x367B) / 16;
            DoorFacing facing = (DoorFacing)((itemID - 0x367B) / 2 % 8);

            door = type switch
            {
              0 => new GenericHouseDoor(facing, 0x367B, 0xED, 0xF4),
              1 => new GenericHouseDoor(facing, 0x368B, 0xEC, 0x3E7),
              _ => door
            };
          }
          else if (itemID >= 0x409B && itemID < 0x40A3)
            door = new GenericHouseDoor(GetSADoorFacing(itemID - 0x409B), itemID, 0xEA, 0xF1, false);
          else if (itemID >= 0x410C && itemID < 0x4114)
            door = new GenericHouseDoor(GetSADoorFacing(itemID - 0x410C), itemID, 0xEA, 0xF1, false);
          else if (itemID >= 0x41C2 && itemID < 0x41CA)
            door = new GenericHouseDoor(GetSADoorFacing(itemID - 0x41C2), itemID, 0xEA, 0xF1, false);
          else if (itemID >= 0x41CF && itemID < 0x41D7)
            door = new GenericHouseDoor(GetSADoorFacing(itemID - 0x41CF), itemID, 0xEA, 0xF1, false);
          else if (itemID >= 0x436E && itemID < 0x437E)
          {
            /* These ones had to be different...
             * Offset		0	2	4	6	8	10	12	14
             * DoorFacing	2	3	2	3	6	7	6	7
             */
            int offset = itemID - 0x436E;
            DoorFacing facing = (DoorFacing)((offset / 2 + 2 * ((1 + offset / 4) % 2)) % 8);
            door = new GenericHouseDoor(facing, itemID, 0xEA, 0xF1, false);
          }
          else if (itemID >= 0x46DD && itemID < 0x46E5)
            door = new GenericHouseDoor(GetSADoorFacing(itemID - 0x46DD), itemID, 0xEB, 0xF2, false);
          else if (itemID >= 0x4D22 && itemID < 0x4D2A)
            door = new GenericHouseDoor(GetSADoorFacing(itemID - 0x4D22), itemID, 0xEA, 0xF1, false);
          else if (itemID >= 0x50C8 && itemID < 0x50D0)
            door = new GenericHouseDoor(GetSADoorFacing(itemID - 0x50C8), itemID, 0xEA, 0xF1, false);
          else if (itemID >= 0x50D0 && itemID < 0x50D8)
            door = new GenericHouseDoor(GetSADoorFacing(itemID - 0x50D0), itemID, 0xEA, 0xF1, false);
          else if (itemID >= 0x5142 && itemID < 0x514A)
            door = new GenericHouseDoor(GetSADoorFacing(itemID - 0x5142), itemID, 0xF0, 0xEF, false);

          if (door != null)
          {
            if (keyValue == 0)
              keyValue = CreateKeys(from);

            door.Locked = true;
            door.KeyValue = keyValue;

            AddDoor(door, mte.m_OffsetX, mte.m_OffsetY, mte.m_OffsetZ);
            Fixtures.Add(door);
          }
        }
      }

      for (int i = 0; i < Fixtures.Count; ++i)
      {
        Item fixture = Fixtures[i];

        if (fixture is HouseTeleporter tp)
        {
          for (int j = 1; j <= Fixtures.Count; ++j)
            if (Fixtures[(i + j) % Fixtures.Count] is HouseTeleporter check && check.ItemID == tp.ItemID)
            {
              tp.Target = check;
              break;
            }
        }
        else if (fixture is BaseHouseDoor door)
        {
          if (door.Link != null)
            continue;

          DoorFacing linkFacing;
          int xOffset, yOffset;

          switch (door.Facing)
          {
            default:
              linkFacing = DoorFacing.EastCCW;
              xOffset = 1;
              yOffset = 0;
              break;
            case DoorFacing.EastCCW:
              linkFacing = DoorFacing.WestCW;
              xOffset = -1;
              yOffset = 0;
              break;
            case DoorFacing.WestCCW:
              linkFacing = DoorFacing.EastCW;
              xOffset = 1;
              yOffset = 0;
              break;
            case DoorFacing.EastCW:
              linkFacing = DoorFacing.WestCCW;
              xOffset = -1;
              yOffset = 0;
              break;
            case DoorFacing.SouthCW:
              linkFacing = DoorFacing.NorthCCW;
              xOffset = 0;
              yOffset = -1;
              break;
            case DoorFacing.NorthCCW:
              linkFacing = DoorFacing.SouthCW;
              xOffset = 0;
              yOffset = 1;
              break;
            case DoorFacing.SouthCCW:
              linkFacing = DoorFacing.NorthCW;
              xOffset = 0;
              yOffset = -1;
              break;
            case DoorFacing.NorthCW:
              linkFacing = DoorFacing.SouthCCW;
              xOffset = 0;
              yOffset = 1;
              break;
            case DoorFacing.SouthSW:
              linkFacing = DoorFacing.SouthSE;
              xOffset = 1;
              yOffset = 0;
              break;
            case DoorFacing.SouthSE:
              linkFacing = DoorFacing.SouthSW;
              xOffset = -1;
              yOffset = 0;
              break;
            case DoorFacing.WestSN:
              linkFacing = DoorFacing.WestSS;
              xOffset = 0;
              yOffset = 1;
              break;
            case DoorFacing.WestSS:
              linkFacing = DoorFacing.WestSN;
              xOffset = 0;
              yOffset = -1;
              break;
          }

          for (int j = i + 1; j < Fixtures.Count; ++j)
            if (Fixtures[j] is BaseHouseDoor check && check.Link == null && check.Facing == linkFacing &&
                check.X - door.X == xOffset && check.Y - door.Y == yOffset && check.Z == door.Z)
            {
              check.Link = door;
              door.Link = check;
              break;
            }
        }
      }
    }

    private static DoorFacing GetSADoorFacing(int offset) => (DoorFacing)((offset / 2 + 2 * (1 + offset / 4)) % 8);

    public void AddFixture(Item item, MultiTileEntry mte)
    {
      Fixtures.Add(item);
      item.MoveToWorld(new Point3D(X + mte.m_OffsetX, Y + mte.m_OffsetY, Z + mte.m_OffsetZ), Map);
    }

    public static void GetFoundationGraphics(FoundationType type, out int east, out int south, out int post,
      out int corner)
    {
      switch (type)
      {
        default:
          corner = 0x0014;
          east = 0x0015;
          south = 0x0016;
          post = 0x0017;
          break;
        case FoundationType.LightWood:
          corner = 0x00BD;
          east = 0x00BE;
          south = 0x00BF;
          post = 0x00C0;
          break;
        case FoundationType.Dungeon:
          corner = 0x02FD;
          east = 0x02FF;
          south = 0x02FE;
          post = 0x0300;
          break;
        case FoundationType.Brick:
          corner = 0x0041;
          east = 0x0043;
          south = 0x0042;
          post = 0x0044;
          break;
        case FoundationType.Stone:
          corner = 0x0065;
          east = 0x0064;
          south = 0x0063;
          post = 0x0066;
          break;

        case FoundationType.ElvenGrey:
          corner = 0x2DF7;
          east = 0x2DF9;
          south = 0x2DFA;
          post = 0x2DF8;
          break;
        case FoundationType.ElvenNatural:
          corner = 0x2DFB;
          east = 0x2DFD;
          south = 0x2DFE;
          post = 0x2DFC;
          break;

        case FoundationType.Crystal:
          corner = 0x3672;
          east = 0x3671;
          south = 0x3670;
          post = 0x3673;
          break;
        case FoundationType.Shadow:
          corner = 0x3676;
          east = 0x3675;
          south = 0x3674;
          post = 0x3677;
          break;
      }
    }

    public static void ApplyFoundation(FoundationType type, MultiComponentList mcl)
    {
      GetFoundationGraphics(type, out int east, out int south, out int post, out int corner);

      int xCenter = mcl.Center.X;
      int yCenter = mcl.Center.Y;

      mcl.Add(post, 0 - xCenter, 0 - yCenter, 0);
      mcl.Add(corner, mcl.Width - 1 - xCenter, mcl.Height - 2 - yCenter, 0);

      for (int x = 1; x < mcl.Width; ++x)
      {
        mcl.Add(south, x - xCenter, 0 - yCenter, 0);

        if (x < mcl.Width - 1)
          mcl.Add(south, x - xCenter, mcl.Height - 2 - yCenter, 0);
      }

      for (int y = 1; y < mcl.Height - 1; ++y)
      {
        mcl.Add(east, 0 - xCenter, y - yCenter, 0);

        if (y < mcl.Height - 2)
          mcl.Add(east, mcl.Width - 1 - xCenter, y - yCenter, 0);
      }
    }

    public static void AddStairsTo(ref MultiComponentList mcl)
    {
      // copy the original..
      mcl = new MultiComponentList(mcl);

      mcl.Resize(mcl.Width, mcl.Height + 1);

      int xCenter = mcl.Center.X;
      int yCenter = mcl.Center.Y;
      int y = mcl.Height - 1;

      for (int x = 0; x < mcl.Width; ++x)
        mcl.Add(0x63, x - xCenter, y - yCenter, 0);
    }

    public MultiComponentList GetEmptyFoundation()
    {
      // Copy original foundation layout
      MultiComponentList mcl = new MultiComponentList(MultiData.GetComponents(ItemID));

      mcl.Resize(mcl.Width, mcl.Height + 1);

      int xCenter = mcl.Center.X;
      int yCenter = mcl.Center.Y;
      int y = mcl.Height - 1;

      ApplyFoundation(Type, mcl);

      for (int x = 1; x < mcl.Width; ++x)
        mcl.Add(0x751, x - xCenter, y - yCenter, 0);

      return mcl;
    }

    public void CheckSignpost()
    {
      MultiComponentList mcl = Components;

      int x = mcl.Min.X;
      int y = mcl.Height - 2 - mcl.Center.Y;

      if (CheckWall(mcl, x, y))
      {
        Signpost?.Delete();

        Signpost = null;
      }
      else if (Signpost == null)
      {
        Signpost = new Static(SignpostGraphic);
        Signpost.MoveToWorld(new Point3D(X + x, Y + y, Z + 7), Map);
      }
      else
      {
        Signpost.ItemID = SignpostGraphic;
        Signpost.MoveToWorld(new Point3D(X + x, Y + y, Z + 7), Map);
      }
    }

    public bool CheckWall(MultiComponentList mcl, int x, int y)
    {
      x += mcl.Center.X;
      y += mcl.Center.Y;

      return x >= 0 && x < mcl.Width && y >= 0 && y < mcl.Height &&
             mcl.Tiles[x][y].Any(tile => tile.Z == 7 && tile.Height == 20);
    }

    public void BeginCustomize(Mobile m)
    {
      if (!m.CheckAlive())
        return;

      if (SpellHelper.CheckCombat(m))
      {
        m.SendLocalizedMessage(1005564, "", 0x22); // Wouldst thou flee during the heat of battle??
        return;
      }

      RelocateEntities();

      foreach (Item item in GetItems()) item.Location = BanLocation;

      foreach (Mobile mobile in GetMobiles().Where(mobile => mobile != m))
        mobile.Location = BanLocation;

      DesignContext.Add(m, this);
      HouseFoundationPackets.SendBeginHouseCustomization(m.NetState, Serial);

      NetState ns = m.NetState;
      if (ns != null)
        SendInfoTo(ns);

      DesignState.SendDetailedInfoTo(ns);
    }

    public override void SendInfoTo(NetState state, bool sendOplPacket)
    {
      base.SendInfoTo(state, sendOplPacket);

      DesignState stateToSend = DesignContext.Find(state.Mobile)?.Foundation == this ? DesignState : CurrentState;
      stateToSend.SendGeneralInfoTo(state);
    }

    public override void Serialize(GenericWriter writer)
    {
      writer.Write(5); // version

      writer.Write(Signpost);
      writer.Write(SignpostGraphic);

      writer.Write((int)Type);

      writer.Write(SignHanger);

      writer.Write(LastRevision);
      writer.Write(Fixtures, true);

      CurrentState.Serialize(writer);
      DesignState.Serialize(writer);
      BackupState.Serialize(writer);

      base.Serialize(writer);
    }

    public override void Deserialize(GenericReader reader)
    {
      int version = reader.ReadInt();

      switch (version)
      {
        case 5:
        case 4:
        {
          Signpost = reader.ReadItem();
          SignpostGraphic = reader.ReadInt();

          goto case 3;
        }
        case 3:
        {
          Type = (FoundationType)reader.ReadInt();

          goto case 2;
        }
        case 2:
        {
          SignHanger = reader.ReadItem();

          goto case 1;
        }
        case 1:
        {
          if (version < 5)
            m_DefaultPrice = reader.ReadInt();

          goto case 0;
        }
        case 0:
        {
          if (version < 3)
            Type = FoundationType.Stone;

          if (version < 4)
            SignpostGraphic = 9;

          LastRevision = reader.ReadInt();
          Fixtures = reader.ReadStrongItemList();

          m_Current = new DesignState(this, reader);
          m_Design = new DesignState(this, reader);
          m_Backup = new DesignState(this, reader);

          break;
        }
      }

      base.Deserialize(reader);
    }

    public bool IsHiddenToCustomizer(Item item) => item == Signpost || item == SignHanger || item == Sign || IsFixture(item);

    public static void Initialize()
    {
      PacketHandlers.RegisterExtended(0x1E, true, QueryDesignDetails);

      PacketHandlers.RegisterEncoded(0x02, true, Designer_Backup);
      PacketHandlers.RegisterEncoded(0x03, true, Designer_Restore);
      PacketHandlers.RegisterEncoded(0x04, true, Designer_Commit);
      PacketHandlers.RegisterEncoded(0x05, true, Designer_Delete);
      PacketHandlers.RegisterEncoded(0x06, true, Designer_Build);
      PacketHandlers.RegisterEncoded(0x0C, true, Designer_Close);
      PacketHandlers.RegisterEncoded(0x0D, true, Designer_Stairs);
      PacketHandlers.RegisterEncoded(0x0E, true, Designer_Sync);
      PacketHandlers.RegisterEncoded(0x10, true, Designer_Clear);
      PacketHandlers.RegisterEncoded(0x12, true, Designer_Level);

      PacketHandlers.RegisterEncoded(0x13, true, Designer_Roof); // Samurai Empire roof
      PacketHandlers.RegisterEncoded(0x14, true, Designer_RoofDelete); // Samurai Empire roof

      PacketHandlers.RegisterEncoded(0x1A, true, Designer_Revert);

      EventSink.Speech += EventSink_Speech;
    }

    private static void EventSink_Speech(SpeechEventArgs e)
    {
      if (DesignContext.Find(e.Mobile) != null)
      {
        e.Mobile.SendLocalizedMessage(1061925); // You cannot speak while customizing your house.
        e.Blocked = true;
      }
    }

    public static void Designer_Sync(NetState state, IEntity e, EncodedReader pvSrc)
    {
      Mobile from = state.Mobile;

      /* Client requested state synchronization
         *  - Resend full house state
         */

      // Resend full house state
      DesignContext.Find(from)?.Foundation.DesignState.SendDetailedInfoTo(state);
    }

    public static void Designer_Clear(NetState state, IEntity e, EncodedReader pvSrc)
    {
      Mobile from = state.Mobile;
      DesignContext context = DesignContext.Find(from);

      if (context == null)
        return;

      /* Client chose to clear the design
         *  - Restore empty foundation
         *     - Construct new design state from empty foundation
         *     - Assign constructed state to foundation
         *  - Update revision
         *  - Update client with new state
         */

      // Restore empty foundation : Construct new design state from empty foundation
      DesignState newDesign = new DesignState(context.Foundation, context.Foundation.GetEmptyFoundation());

      // Restore empty foundation : Assign constructed state to foundation
      context.Foundation.DesignState = newDesign;

      // Update revision
      newDesign.OnRevised();

      // Update client with new state
      context.Foundation.SendInfoTo(state);
      newDesign.SendDetailedInfoTo(state);
    }

    public static void Designer_Restore(NetState state, IEntity e, EncodedReader pvSrc)
    {
      Mobile from = state.Mobile;
      DesignContext context = DesignContext.Find(from);

      if (context == null)
        return;

      /* Client chose to restore design to the last backup state
         *  - Restore backup
         *     - Construct new design state from backup state
         *     - Assign constructed state to foundation
         *  - Update revision
         *  - Update client with new state
         */

      // Restore backup : Construct new design state from backup state
      DesignState backupDesign = new DesignState(context.Foundation.BackupState);

      // Restore backup : Assign constructed state to foundation
      context.Foundation.DesignState = backupDesign;

      // Update revision;
      backupDesign.OnRevised();

      // Update client with new state
      context.Foundation.SendInfoTo(state);
      backupDesign.SendDetailedInfoTo(state);
    }

    public static void Designer_Backup(NetState state, IEntity e, EncodedReader pvSrc)
    {
      Mobile from = state.Mobile;
      DesignContext context = DesignContext.Find(from);

      if (context == null)
        return;

      /* Client chose to backup design state
         *  - Construct a copy of the current design state
         *  - Assign constructed state to backup state field
         */

      // Construct a copy of the current design state
      DesignState copyState = new DesignState(context.Foundation.DesignState);

      // Assign constructed state to backup state field
      context.Foundation.BackupState = copyState;
    }

    public static void Designer_Revert(NetState state, IEntity e, EncodedReader pvSrc)
    {
      Mobile from = state.Mobile;
      DesignContext context = DesignContext.Find(from);

      if (context == null)
        return;

      /* Client chose to revert design state to currently visible state
         *  - Revert design state
         *     - Construct a copy of the current visible state
         *     - Freeze fixtures in constructed state
         *     - Assign constructed state to foundation
         *     - If a signpost is needed, add it
         *  - Update revision
         *  - Update client with new state
         */

      // Revert design state : Construct a copy of the current visible state
      DesignState copyState = new DesignState(context.Foundation.CurrentState);

      // Revert design state : Freeze fixtures in constructed state
      copyState.FreezeFixtures();

      // Revert design state : Assign constructed state to foundation
      context.Foundation.DesignState = copyState;

      // Revert design state : If a signpost is needed, add it
      context.Foundation.CheckSignpost();

      // Update revision
      copyState.OnRevised();

      // Update client with new state
      context.Foundation.SendInfoTo(state);
      copyState.SendDetailedInfoTo(state);
    }

    public void EndConfirmCommit(Mobile from)
    {
      int oldPrice = Price;
      int newPrice = oldPrice + CustomizationCost +
                     (DesignState.Components.List.Length -
                      (CurrentState.Components.List.Length + CurrentState.Fixtures.Length)) * 500;
      int cost = newPrice - oldPrice;

      if (!Deleted)
      {
        // Temporary Fix. We should be booting a client out of customization mode in the delete handler.
        if (from.AccessLevel >= AccessLevel.GameMaster && cost != 0)
          from.SendMessage("{0} gold would have been {1} your bank if you were not a GM.", cost.ToString(),
            cost > 0 ? "withdrawn from" : "deposited into");
        else
        {
          if (cost > 0)
          {
            if (Banker.Withdraw(from, cost))
              from.SendLocalizedMessage(1060398,
                cost.ToString()); // ~1_AMOUNT~ gold has been withdrawn from your bank box.
            else
            {
              from.SendLocalizedMessage(
                1061903); // You cannot commit this house design, because you do not have the necessary funds in your bank box to pay for the upgrade.  Please back up your design, obtain the required funds, and commit your design again.
              return;
            }
          }
          else if (cost < 0)
          {
            if (Banker.Deposit(from, -cost))
              from.SendLocalizedMessage(1060397,
                (-cost).ToString()); // ~1_AMOUNT~ gold has been deposited into your bank box.
            else
              return;
          }
        }
      }

      /* Client chose to commit current design state
         *  - Commit design state
         *     - Construct a copy of the current design state
         *     - Clear visible fixtures
         *     - Melt fixtures from constructed state
         *     - Add melted fixtures from constructed state
         *     - Assign constructed state to foundation
         *  - Update house price
         *  - Remove design context
         *  - Notify the client that customization has ended
         *  - Notify the core that the foundation has changed and should be resent to all clients
         *  - If a signpost is needed, add it
         *  - Eject all from house
         *  - Restore relocated entities
         */

      // Commit design state : Construct a copy of the current design state
      DesignState copyState = new DesignState(DesignState);

      // Commit design state : Clear visible fixtures
      ClearFixtures(from);

      // Commit design state : Melt fixtures from constructed state
      copyState.MeltFixtures();

      // Commit design state : Add melted fixtures from constructed state
      AddFixtures(from, copyState.Fixtures);

      // Commit design state : Assign constructed state to foundation
      CurrentState = copyState;

      // Update house price
      Price = newPrice - CustomizationCost;

      // Remove design context
      DesignContext.Remove(from);

      // Notify the client that customization has ended
      HouseFoundationPackets.SendEndHouseCustomization(from.NetState, Serial);

      // Notify the core that the foundation has changed and should be resent to all clients
      Delta(ItemDelta.Update);
      ProcessDelta();
      CurrentState.SendDetailedInfoTo(from.NetState);

      // If a signpost is needed, add it
      CheckSignpost();

      // Eject all from house
      from.RevealingAction();

      foreach (Item item in GetItems())
        item.Location = BanLocation;

      foreach (Mobile mobile in GetMobiles())
        mobile.Location = BanLocation;

      // Restore relocated entities
      RestoreRelocatedEntities();
    }

    public static void Designer_Commit(NetState state, IEntity e, EncodedReader pvSrc)
    {
      Mobile from = state.Mobile;
      DesignContext context = DesignContext.Find(from);

      if (context != null)
      {
        int oldPrice = context.Foundation.Price;
        int newPrice = oldPrice + context.Foundation.CustomizationCost +
                       (context.Foundation.DesignState.Components.List.Length -
                        (context.Foundation.CurrentState.Components.List.Length +
                         context.Foundation.Fixtures.Count)) * 500;
        int bankBalance = Banker.GetBalance(from);

        from.SendGump(new ConfirmCommitGump(from, context.Foundation, bankBalance, oldPrice, newPrice));
      }
    }

    public static int GetLevelZ(int level, HouseFoundation house)
    {
      if (level < 1 || level > house.MaxLevels)
        level = 1;

      return (level - 1) * 20 + 7;
    }

    public static int GetZLevel(int z, HouseFoundation house)
    {
      int level = (z - 7) / 20 + 1;

      if (level < 1 || level > house.MaxLevels)
        level = 1;

      return level;
    }

    public static bool ValidPiece(int itemID, bool roof = false)
    {
      itemID &= TileData.MaxItemValue;
      return roof != ((TileData.ItemTable[itemID].Flags & TileFlag.Roof) == 0) && Verification.IsItemValid(itemID);
    }

    public static bool IsStairBlock(int id)
    {
      int delta = -1;

      for (int i = 0; delta < 0 && i < m_BlockIDs.Length; ++i)
        delta = m_BlockIDs[i] - id;

      return delta == 0;
    }

    public static bool IsStair(int id, ref int dir)
    {
      //dir n=0 w=1 s=2 e=3
      int delta = -4;

      for (int i = 0; delta < -3 && i < m_StairSeqs.Length; ++i)
        delta = m_StairSeqs[i] - id;

      if (delta >= -3 && delta <= 0)
      {
        dir = -delta;
        return true;
      }

      for (int i = 0; i < m_StairIDs.Length; ++i)
        if (m_StairIDs[i] == id)
        {
          dir = i % 4;
          return true;
        }

      return false;
    }

    public static bool DeleteStairs(MultiComponentList mcl, int id, int x, int y, int z)
    {
      int ax = x + mcl.Center.X;
      int ay = y + mcl.Center.Y;

      if (ax < 0 || ay < 0 || ax >= mcl.Width || ay >= mcl.Height - 1 || z < 7 || (z - 7) % 5 != 0)
        return false;

      if (IsStairBlock(id))
      {
        StaticTile[] tiles = mcl.Tiles[ax][ay];

        for (int i = 0; i < tiles.Length; ++i)
        {
          StaticTile tile = tiles[i];

          if (tile.Z == z + 5)
          {
            id = tile.ID;
            z = tile.Z;

            if (!IsStairBlock(id))
              break;
          }
        }
      }

      int dir = 0;

      if (!IsStair(id, ref dir))
        return false;

      if (AllowStairSectioning)
        return true; // skip deletion

      int height = (z - 7) % 20 / 5;

      int xStart, yStart;
      int xInc, yInc;

      switch (dir)
      {
        default: // North
        {
          xStart = x;
          yStart = y + height;
          xInc = 0;
          yInc = -1;
          break;
        }
        case 1: // West
        {
          xStart = x + height;
          yStart = y;
          xInc = -1;
          yInc = 0;
          break;
        }
        case 2: // South
        {
          xStart = x;
          yStart = y - height;
          xInc = 0;
          yInc = 1;
          break;
        }
        case 3: // East
        {
          xStart = x - height;
          yStart = y;
          xInc = 1;
          yInc = 0;
          break;
        }
      }

      int zStart = z - height * 5;

      for (int i = 0; i < 4; ++i)
      {
        x = xStart + i * xInc;
        y = yStart + i * yInc;

        for (int j = 0; j <= i; ++j)
          mcl.RemoveXYZH(x, y, zStart + j * 5, 5);

        ax = x + mcl.Center.X;
        ay = y + mcl.Center.Y;

        if (ax >= 1 && ax < mcl.Width && ay >= 1 && ay < mcl.Height - 1)
        {
          StaticTile[] tiles = mcl.Tiles[ax][ay];

          bool hasBaseFloor = false;

          for (int j = 0; !hasBaseFloor && j < tiles.Length; ++j)
            hasBaseFloor = tiles[j].Z == 7 && tiles[j].ID != 1;

          if (!hasBaseFloor)
            mcl.Add(0x31F4, x, y, 7);
        }
      }

      return true;
    }

    public static void Designer_Delete(NetState state, IEntity e, EncodedReader pvSrc)
    {
      Mobile from = state.Mobile;
      DesignContext context = DesignContext.Find(from);

      if (context == null)
        return;

      /* Client chose to delete a component
         *  - Read data detailing which component to delete
         *  - Verify component is deletable
         *  - Remove the component
         *  - If needed, replace removed component with a dirt tile
         *  - Update revision
         */

      // Read data detailing which component to delete
      int itemID = pvSrc.ReadInt32();
      int x = pvSrc.ReadInt32();
      int y = pvSrc.ReadInt32();
      int z = pvSrc.ReadInt32();

      // Verify component is deletable
      DesignState design = context.Foundation.DesignState;
      MultiComponentList mcl = design.Components;

      int ax = x + mcl.Center.X;
      int ay = y + mcl.Center.Y;

      if (z == 0 && ax >= 0 && ax < mcl.Width && ay >= 0 && ay < mcl.Height - 1)
      {
        /* Component is not deletable
           *  - Resend design state
           *  - Return without further processing
           */

        design.SendDetailedInfoTo(state);
        return;
      }

      bool fixState = false;

      // Remove the component
      if (AllowStairSectioning)
      {
        fixState |= DeleteStairs(mcl, itemID, x, y, z); // The client removes the entire set of stairs locally, resend state

        mcl.Remove(itemID, x, y, z);
      }
      else if (!DeleteStairs(mcl, itemID, x, y, z))
        mcl.Remove(itemID, x, y, z);

      // If needed, replace removed component with a dirt tile
      if (ax >= 1 && ax < mcl.Width && ay >= 1 && ay < mcl.Height - 1)
      {
        StaticTile[] tiles = mcl.Tiles[ax][ay];

        bool hasBaseFloor = false;

        for (int i = 0; !hasBaseFloor && i < tiles.Length; ++i)
          hasBaseFloor = tiles[i].Z == 7 && tiles[i].ID != 1;

        if (!hasBaseFloor) mcl.Add(0x31F4, x, y, 7);
      }

      // Update revision
      design.OnRevised();

      // Resend design state
      if (fixState)
        design.SendDetailedInfoTo(state);
    }

    public static void Designer_Stairs(NetState state, IEntity e, EncodedReader pvSrc)
    {
      Mobile from = state.Mobile;
      DesignContext context = DesignContext.Find(from);

      if (context == null)
        return;

      /* Client chose to add stairs
         *  - Read data detailing stair type and location
         *  - Validate stair multi ID
         *  - Add the stairs
         *     - Load data describing the stair components
         *     - Insert described components
         *  - Update revision
         */

      // Read data detailing stair type and location
      int itemID = pvSrc.ReadInt32();
      int x = pvSrc.ReadInt32();
      int y = pvSrc.ReadInt32();

      // Validate stair multi ID
      DesignState design = context.Foundation.DesignState;

      if (!Verification.IsMultiValid(itemID))
      {
        /* Specified multi ID is not a stair
           *  - Resend design state
           *  - Return without further processing
           */

        TraceValidity(state, itemID);
        design.SendDetailedInfoTo(state);
        return;
      }

      // Add the stairs
      MultiComponentList mcl = design.Components;

      // Add the stairs : Load data describing stair components
      MultiComponentList stairs = MultiData.GetComponents(itemID);

      // Add the stairs : Insert described components
      int z = GetLevelZ(context.Level, context.Foundation);

      for (int i = 0; i < stairs.List.Length; ++i)
      {
        MultiTileEntry entry = stairs.List[i];

        if (entry.m_ItemID != 1)
          mcl.Add(entry.m_ItemID, x + entry.m_OffsetX, y + entry.m_OffsetY, z + entry.m_OffsetZ);
      }

      // Update revision
      design.OnRevised();
    }

    private static void TraceValidity(NetState state, int itemID)
    {
      try
      {
        using StreamWriter op = new StreamWriter("comp_val.log", true);
        op.WriteLine("{0}\t{1}\tInvalid ItemID 0x{2:X4}", state, state.Mobile, itemID);
      }
      catch
      {
        // ignored
      }
    }

    public static void Designer_Build(NetState state, IEntity e, EncodedReader pvSrc)
    {
      Mobile from = state.Mobile;
      DesignContext context = DesignContext.Find(from);

      if (context == null)
        return;

      /* Client chose to add a component
         *  - Read data detailing component graphic and location
         *  - Add component
         *  - Update revision
         */

      // Read data detailing component graphic and location
      int itemID = pvSrc.ReadInt32();
      int x = pvSrc.ReadInt32();
      int y = pvSrc.ReadInt32();

      // Add component
      DesignState design = context.Foundation.DesignState;

      if (from.AccessLevel < AccessLevel.GameMaster && !ValidPiece(itemID))
      {
        TraceValidity(state, itemID);
        design.SendDetailedInfoTo(state);
        return;
      }

      MultiComponentList mcl = design.Components;

      int z = GetLevelZ(context.Level, context.Foundation);

      if (y + mcl.Center.Y == mcl.Height - 1)
        z = 0; // Tiles placed on the far-south of the house are at 0 Z

      mcl.Add(itemID, x, y, z);

      // Update revision
      design.OnRevised();
    }

    public static void Designer_Close(NetState state, IEntity e, EncodedReader pvSrc)
    {
      Mobile from = state.Mobile;
      DesignContext context = DesignContext.Find(from);

      if (context == null)
        return;

      /* Client closed his house design window
         *  - Remove design context
         *  - Notify the client that customization has ended
         *  - Refresh client with current visible design state
         *  - If a signpost is needed, add it
         *  - Eject all from house
         *  - Restore relocated entities
         */

      // Remove design context
      DesignContext.Remove(from);

      // Notify the client that customization has ended

      HouseFoundationPackets.SendEndHouseCustomization(from.NetState, context.Foundation.Serial);

      // Refresh client with current visible design state
      context.Foundation.SendInfoTo(state);
      context.Foundation.CurrentState.SendDetailedInfoTo(state);

      // If a signpost is needed, add it
      context.Foundation.CheckSignpost();

      // Eject all from house
      from.RevealingAction();

      foreach (Item item in context.Foundation.GetItems())
        item.Location = context.Foundation.BanLocation;

      foreach (Mobile mobile in context.Foundation.GetMobiles())
        mobile.Location = context.Foundation.BanLocation;

      // Restore relocated entities
      context.Foundation.RestoreRelocatedEntities();
    }

    public static void Designer_Level(NetState state, IEntity e, EncodedReader pvSrc)
    {
      Mobile from = state.Mobile;
      DesignContext context = DesignContext.Find(from);

      if (context == null)
        return;

      /* Client is moving to a new floor level
         *  - Read data detailing the target level
         *  - Validate target level
         *  - Update design context with new level
         *  - Teleport mobile to new level
         *  - Update client
         *
         */

      // Read data detailing the target level
      int newLevel = pvSrc.ReadInt32();

      // Validate target level
      if (newLevel < 1 || newLevel > context.MaxLevels)
        newLevel = 1;

      // Update design context with new level
      context.Level = newLevel;

      // Teleport mobile to new level
      from.Location = new Point3D(from.X, from.Y, context.Foundation.Z + GetLevelZ(newLevel, context.Foundation));

      // Update client
      context.Foundation.SendInfoTo(state);
    }

    public static void QueryDesignDetails(NetState state, PacketReader pvSrc)
    {
      Mobile from = state.Mobile;

      if (World.FindItem(pvSrc.ReadUInt32()) is HouseFoundation foundation && from.Map == foundation.Map && from.InRange(foundation.GetWorldLocation(), 24) &&
          from.CanSee(foundation))
      {
        DesignState stateToSend = DesignContext.Find(from)?.Foundation == foundation ? foundation.DesignState : foundation.CurrentState;
        stateToSend.SendDetailedInfoTo(state);
      }
    }

    public static void Designer_Roof(NetState state, IEntity e, EncodedReader pvSrc)
    {
      Mobile from = state.Mobile;
      DesignContext context = DesignContext.Find(from);

      if (context == null || !Core.SE && from.AccessLevel < AccessLevel.GameMaster)
        return;

      // Read data detailing component graphic and location
      int itemID = pvSrc.ReadInt32();
      int x = pvSrc.ReadInt32();
      int y = pvSrc.ReadInt32();
      int z = pvSrc.ReadInt32();

      // Add component
      DesignState design = context.Foundation.DesignState;

      if (from.AccessLevel < AccessLevel.GameMaster && !ValidPiece(itemID, true))
      {
        TraceValidity(state, itemID);
        design.SendDetailedInfoTo(state);
        return;
      }

      MultiComponentList mcl = design.Components;

      if (z < -3 || z > 12 || z % 3 != 0)
        z = -3;
      z += GetLevelZ(context.Level, context.Foundation);

      MultiTileEntry[] list = mcl.List;
      for (int i = 0; i < list.Length; i++)
      {
        MultiTileEntry mte = list[i];

        if (mte.m_OffsetX == x && mte.m_OffsetY == y &&
            GetZLevel(mte.m_OffsetZ, context.Foundation) == context.Level &&
            (TileData.ItemTable[mte.m_ItemID & TileData.MaxItemValue].Flags & TileFlag.Roof) != 0)
          mcl.Remove(mte.m_ItemID, x, y, mte.m_OffsetZ);
      }

      mcl.Add(itemID, x, y, z);

      // Update revision
      design.OnRevised();
    }

    public static void Designer_RoofDelete(NetState state, IEntity e, EncodedReader pvSrc)
    {
      Mobile from = state.Mobile;
      DesignContext context = DesignContext.Find(from);

      // No need to check for Core.SE if trying to remove something that shouldn't be able to be placed anyways
      if (context == null)
        return;

      // Read data detailing which component to delete
      int itemID = pvSrc.ReadInt32();
      int x = pvSrc.ReadInt32();
      int y = pvSrc.ReadInt32();
      int z = pvSrc.ReadInt32();

      // Verify component is deletable
      DesignState design = context.Foundation.DesignState;
      MultiComponentList mcl = design.Components;

      if ((TileData.ItemTable[itemID & TileData.MaxItemValue].Flags & TileFlag.Roof) == 0)
      {
        design.SendDetailedInfoTo(state);
        return;
      }

      mcl.Remove(itemID, x, y, z);

      design.OnRevised();
    }
  }

  public class DesignState
  {
    private int m_Revision;
    private byte[] m_Packet;

    public DesignState(HouseFoundation foundation, MultiComponentList components)
    {
      Foundation = foundation;
      Components = components;
      Fixtures = new MultiTileEntry[0];
    }

    public DesignState(DesignState toCopy)
    {
      Foundation = toCopy.Foundation;
      Components = new MultiComponentList(toCopy.Components);
      Revision = toCopy.Revision;
      Fixtures = new MultiTileEntry[toCopy.Fixtures.Length];

      for (int i = 0; i < Fixtures.Length; ++i)
        Fixtures[i] = toCopy.Fixtures[i];
    }

    public DesignState(HouseFoundation foundation, GenericReader reader)
    {
      Foundation = foundation;

      int version = reader.ReadInt();

      switch (version)
      {
        case 0:
        {
          Components = new MultiComponentList(reader);

          int length = reader.ReadInt();

          Fixtures = new MultiTileEntry[length];

          for (int i = 0; i < length; ++i)
          {
            Fixtures[i].m_ItemID = reader.ReadUShort();
            Fixtures[i].m_OffsetX = reader.ReadShort();
            Fixtures[i].m_OffsetY = reader.ReadShort();
            Fixtures[i].m_OffsetZ = reader.ReadShort();
            Fixtures[i].m_Flags = reader.ReadInt();
          }

          Revision = reader.ReadInt();

          break;
        }
      }
    }

    public HouseFoundation Foundation{ get; }

    public MultiComponentList Components{ get; }

    public MultiTileEntry[] Fixtures{ get; private set; }

    public int Revision
    {
      get => m_Revision;
      set => m_Revision = value;
    }

    public byte[] Packet
    {
      get => m_Packet;
      set => Interlocked.Exchange(ref m_Packet, value);
    }

    public void Serialize(GenericWriter writer)
    {
      writer.Write(0); // version

      Components.Serialize(writer);

      writer.Write(Fixtures.Length);

      for (int i = 0; i < Fixtures.Length; ++i)
      {
        MultiTileEntry ent = Fixtures[i];

        writer.Write(ent.m_ItemID);
        writer.Write(ent.m_OffsetX);
        writer.Write(ent.m_OffsetY);
        writer.Write(ent.m_OffsetZ);
        writer.Write(ent.m_Flags);
      }

      writer.Write(Revision);
    }

    public void OnRevised()
    {
      Interlocked.Exchange(ref m_Revision, ++Foundation.LastRevision);
    }

    public void SendGeneralInfoTo(NetState state)
    {
      HouseFoundationPackets.SendDesignStateGeneral(state, Foundation.Serial, Revision);
    }

    public void SendDetailedInfoTo(NetState state)
    {
      if (state != null)
        HouseFoundationPackets.SendDesignDetails(state, Foundation, this);
    }

    public void FreezeFixtures()
    {
      OnRevised();

      for (int i = 0; i < Fixtures.Length; ++i)
      {
        MultiTileEntry mte = Fixtures[i];

        Components.Add(mte.m_ItemID, mte.m_OffsetX, mte.m_OffsetY, mte.m_OffsetZ);
      }

      Fixtures = new MultiTileEntry[0];
    }

    public void MeltFixtures()
    {
      OnRevised();

      MultiTileEntry[] list = Components.List;
      int length = 0;

      for (int i = list.Length - 1; i >= 0; --i)
      {
        MultiTileEntry mte = list[i];

        if (IsFixture(mte.m_ItemID))
          ++length;
      }

      Fixtures = new MultiTileEntry[length];

      for (int i = list.Length - 1; i >= 0; --i)
      {
        MultiTileEntry mte = list[i];

        if (IsFixture(mte.m_ItemID))
        {
          Fixtures[--length] = mte;
          Components.Remove(mte.m_ItemID, mte.m_OffsetX, mte.m_OffsetY, mte.m_OffsetZ);
        }
      }
    }

    public static bool IsFixture(int itemID) =>
      itemID >= 0x675 && itemID < 0x6F5 ||
      itemID >= 0x314 && itemID < 0x364 ||
      itemID >= 0x824 && itemID < 0x834 ||
      itemID >= 0x839 && itemID < 0x849 ||
      itemID >= 0x84C && itemID < 0x85C ||
      itemID >= 0x866 && itemID < 0x876 ||
      itemID >= 0x0E8 && itemID < 0x0F8 ||
      itemID >= 0x1FED && itemID < 0x1FFD ||
      itemID >= 0x181D && itemID < 0x1829 ||
      itemID >= 0x241F && itemID < 0x2421 ||
      itemID >= 0x2423 && itemID < 0x2425 ||
      itemID >= 0x2A05 && itemID < 0x2A1D ||
      itemID >= 0x319C && itemID < 0x31B0 ||
      itemID == 0x2D46 || itemID == 0x2D48 ||
      itemID == 0x2FE2 || itemID == 0x2FE4 ||
      itemID >= 0x2D63 && itemID < 0x2D70 ||
      itemID >= 0x319C && itemID < 0x31AF ||
      itemID >= 0x367B && itemID < 0x369B ||
      itemID >= 0x409B && itemID < 0x40A3 ||
      itemID >= 0x410C && itemID < 0x4114 ||
      itemID >= 0x41C2 && itemID < 0x41CA ||
      itemID >= 0x41CF && itemID < 0x41D7 ||
      itemID >= 0x436E && itemID < 0x437E ||
      itemID >= 0x46DD && itemID < 0x46E5 ||
      itemID >= 0x4D22 && itemID < 0x4D2A ||
      itemID >= 0x50C8 && itemID < 0x50D8 ||
      itemID >= 0x5142 && itemID < 0x514A ||
      itemID >= 0x9AD7 && itemID < 0x9AE7 ||
      itemID >= 0x9B3C && itemID < 0x9B4C;
  }

  public class ConfirmCommitGump : Gump
  {
    private HouseFoundation m_Foundation;

    public ConfirmCommitGump(Mobile from, HouseFoundation foundation, int bankBalance, int oldPrice, int newPrice)
      : base(50, 50)
    {
      m_Foundation = foundation;

      AddPage(0);

      AddBackground(0, 0, 320, 320, 5054);

      AddImageTiled(10, 10, 300, 20, 2624);
      AddImageTiled(10, 40, 300, 240, 2624);
      AddImageTiled(10, 290, 300, 20, 2624);

      AddAlphaRegion(10, 10, 300, 300);

      AddHtmlLocalized(10, 10, 300, 20, 1062060, 32736); // <CENTER>COMMIT DESIGN</CENTER>

      AddHtmlLocalized(10, 40, 300, 140, newPrice - oldPrice <= bankBalance ? 1061898 : 1061903, 1023, false, true);

      AddHtmlLocalized(10, 190, 150, 20, 1061902, 32736); // Bank Balance:
      AddLabel(170, 190, 55, bankBalance.ToString());

      AddHtmlLocalized(10, 215, 150, 20, 1061899, 1023); // Old Value:
      AddLabel(170, 215, 90, oldPrice.ToString());

      AddHtmlLocalized(10, 235, 150, 20, 1061900, 1023); // Cost To Commit:
      AddLabel(170, 235, 90, newPrice.ToString());

      if (newPrice - oldPrice < 0)
      {
        AddHtmlLocalized(10, 260, 150, 20, 1062059, 992); // Your Refund:
        AddLabel(170, 260, 70, (oldPrice - newPrice).ToString());
      }
      else
      {
        AddHtmlLocalized(10, 260, 150, 20, 1061901, 31744); // Your Cost:
        AddLabel(170, 260, 40, (newPrice - oldPrice).ToString());
      }

      AddButton(10, 290, 4005, 4007, 1);
      AddHtmlLocalized(45, 290, 55, 20, 1011036, 32767); // OKAY

      AddButton(170, 290, 4005, 4007, 0);
      AddHtmlLocalized(195, 290, 55, 20, 1011012, 32767); // CANCEL
    }

    public override void OnResponse(NetState sender, RelayInfo info)
    {
      if (info.ButtonID == 1)
        m_Foundation.EndConfirmCommit(sender.Mobile);
    }
  }

  public class DesignContext
  {
    public DesignContext(HouseFoundation foundation)
    {
      Foundation = foundation;
      Level = 1;
    }

    public HouseFoundation Foundation{ get; }

    public int Level{ get; set; }

    public int MaxLevels => Foundation.MaxLevels;

    public static Dictionary<Mobile, DesignContext> Table{ get; } = new Dictionary<Mobile, DesignContext>();

    public static DesignContext Find(Mobile from)
    {
      if (from == null)
        return null;

      Table.TryGetValue(from, out DesignContext d);

      return d;
    }

    public static bool Check(Mobile m)
    {
      if (Find(m) == null)
        return true;

      m.SendLocalizedMessage(1062206); // You cannot do that while customizing a house.
      return false;

    }

    public static void Add(Mobile from, HouseFoundation foundation)
    {
      if (from == null)
        return;

      DesignContext c = new DesignContext(foundation);

      Table[from] = c;

      if (from is PlayerMobile pm)
        pm.DesignContext = c;

      foundation.Customizer = from;

      from.Hidden = true;
      from.Location = new Point3D(foundation.X, foundation.Y, foundation.Z + 7);

      NetState state = from.NetState;

      if (state == null)
        return;

      List<Item> fixtures = foundation.Fixtures;

      for (int i = 0; fixtures != null && i < fixtures.Count; ++i)
      {
        Item item = fixtures[i];

        Packets.SendRemoveEntity(state, item.Serial);
      }

      if (foundation.Signpost != null)
        Packets.SendRemoveEntity(state, foundation.Signpost.Serial);

      if (foundation.SignHanger != null)
        Packets.SendRemoveEntity(state, foundation.SignHanger.Serial);

      if (foundation.Sign != null)
        Packets.SendRemoveEntity(state, foundation.Sign.Serial);
    }

    public static void Remove(Mobile from)
    {
      DesignContext context = Find(from);

      if (context == null)
        return;

      Table.Remove(from);

      if (from is PlayerMobile pm)
        pm.DesignContext = null;

      context.Foundation.Customizer = null;

      NetState state = from.NetState;

      if (state == null)
        return;

      List<Item> fixtures = context.Foundation.Fixtures;

      for (int i = 0; fixtures != null && i < fixtures.Count; ++i)
      {
        Item item = fixtures[i];

        item.SendInfoTo(state);
      }

      context.Foundation.Signpost?.SendInfoTo(state);
      context.Foundation.SignHanger?.SendInfoTo(state);
      context.Foundation.Sign?.SendInfoTo(state);
    }
  }

  public class BeginHouseCustomization : Packet
  {
    public BeginHouseCustomization(HouseFoundation house)
      : base(0xBF)
    {
      EnsureCapacity(17);

      m_Stream.Write((short)0x20);
      m_Stream.Write(house.Serial);
      m_Stream.Write((byte)0x04);
      m_Stream.Write((ushort)0x0000);
      m_Stream.Write((ushort)0xFFFF);
      m_Stream.Write((ushort)0xFFFF);
      m_Stream.Write((byte)0xFF);
    }
  }

  public class EndHouseCustomization : Packet
  {
    public EndHouseCustomization(HouseFoundation house)
      : base(0xBF)
    {
      EnsureCapacity(17);

      m_Stream.Write((short)0x20);
      m_Stream.Write(house.Serial);
      m_Stream.Write((byte)0x05);
      m_Stream.Write((ushort)0x0000);
      m_Stream.Write((ushort)0xFFFF);
      m_Stream.Write((ushort)0xFFFF);
      m_Stream.Write((byte)0xFF);
    }
  }

  public sealed class DesignStateGeneral : Packet
  {
    public DesignStateGeneral(HouseFoundation house, DesignState state)
      : base(0xBF)
    {
      EnsureCapacity(13);

      m_Stream.Write((short)0x1D);
      m_Stream.Write(house.Serial);
      m_Stream.Write(state.Revision);
    }
  }

  public sealed class DesignStateDetailed : Packet
  {
    public const int MaxItemsPerStairBuffer = 750;

    private static ConcurrentQueue<SendQueueEntry> m_SendQueue;
    private static AutoResetEvent m_Sync;

    private byte[][] m_PlaneBuffers;

    private bool[] m_PlaneUsed = new bool[9];
    private byte[] m_PrimBuffer = new byte[4];
    private byte[][] m_StairBuffers;

    static DesignStateDetailed()
    {
      m_SendQueue = new ConcurrentQueue<SendQueueEntry>();
      m_Sync = new AutoResetEvent(false);

      Task.Run(ProcessCompression);
    }

    public DesignStateDetailed(uint serial, int revision, int xMin, int yMin, int xMax, int yMax, MultiTileEntry[] tiles)
      : base(0xD8)
    {
      EnsureCapacity(17 + tiles.Length * 5);

      Write((byte)0x03); // Compression Type
      Write((byte)0x00); // Unknown
      Write(serial);
      Write(revision);
      Write((short)tiles.Length);
      Write((short)0); // Buffer length : reserved
      Write((byte)0); // Plane count : reserved

      int totalLength = 1; // includes plane count

      int width = xMax - xMin + 1;
      int height = yMax - yMin + 1;

      m_PlaneBuffers = new byte[9][];

      for (int i = 0; i < m_PlaneBuffers.Length; ++i)
        m_PlaneBuffers[i] = ArrayPool<byte>.Shared.Rent(0x400);

      m_StairBuffers = new byte[6][];

      for (int i = 0; i < m_StairBuffers.Length; ++i)
        m_StairBuffers[i] = ArrayPool<byte>.Shared.Rent(MaxItemsPerStairBuffer * 5);

      Clear(m_PlaneBuffers[0], width * height * 2);

      for (int i = 0; i < 4; ++i)
      {
        Clear(m_PlaneBuffers[1 + i], (width - 1) * (height - 2) * 2);
        Clear(m_PlaneBuffers[5 + i], width * (height - 1) * 2);
      }

      int totalStairsUsed = 0;

      for (int i = 0; i < tiles.Length; ++i)
      {
        MultiTileEntry mte = tiles[i];
        int x = mte.m_OffsetX - xMin;
        int y = mte.m_OffsetY - yMin;
        int z = mte.m_OffsetZ;
        bool floor = TileData.ItemTable[mte.m_ItemID & TileData.MaxItemValue].Height <= 0;
        int plane, size;

        switch (z)
        {
          case 0:
            plane = 0;
            break;
          case 7:
            plane = 1;
            break;
          case 27:
            plane = 2;
            break;
          case 47:
            plane = 3;
            break;
          case 67:
            plane = 4;
            break;
          default:
          {
            int stairBufferIndex = totalStairsUsed / MaxItemsPerStairBuffer;
            byte[] stairBuffer = m_StairBuffers[stairBufferIndex];

            int byteIndex = totalStairsUsed % MaxItemsPerStairBuffer * 5;

            stairBuffer[byteIndex++] = (byte)(mte.m_ItemID >> 8);
            stairBuffer[byteIndex++] = (byte)mte.m_ItemID;

            stairBuffer[byteIndex++] = (byte)mte.m_OffsetX;
            stairBuffer[byteIndex++] = (byte)mte.m_OffsetY;
            stairBuffer[byteIndex++] = (byte)mte.m_OffsetZ;

            ++totalStairsUsed;

            continue;
          }
        }

        if (plane == 0)
        {
          size = height;
        }
        else if (floor)
        {
          size = height - 2;
          x -= 1;
          y -= 1;
        }
        else
        {
          size = height - 1;
          plane += 4;
        }

        int index = (x * size + y) * 2;

        if (x < 0 || y < 0 || y >= size || index + 1 >= 0x400)
        {
          int stairBufferIndex = totalStairsUsed / MaxItemsPerStairBuffer;
          byte[] stairBuffer = m_StairBuffers[stairBufferIndex];

          int byteIndex = totalStairsUsed % MaxItemsPerStairBuffer * 5;

          stairBuffer[byteIndex++] = (byte)(mte.m_ItemID >> 8);
          stairBuffer[byteIndex++] = (byte)mte.m_ItemID;

          stairBuffer[byteIndex++] = (byte)mte.m_OffsetX;
          stairBuffer[byteIndex++] = (byte)mte.m_OffsetY;
          stairBuffer[byteIndex++] = (byte)mte.m_OffsetZ;

          ++totalStairsUsed;
        }
        else
        {
          m_PlaneUsed[plane] = true;
          m_PlaneBuffers[plane][index] = (byte)(mte.m_ItemID >> 8);
          m_PlaneBuffers[plane][index + 1] = (byte)mte.m_ItemID;
        }
      }

      int planeCount = 0;

      byte[] m_DeflatedBuffer = ArrayPool<byte>.Shared.Rent(0x2000);

      for (int i = 0; i < m_PlaneBuffers.Length; ++i)
      {
        if (!m_PlaneUsed[i])
        {
          ArrayPool<byte>.Shared.Return(m_PlaneBuffers[i]);
          continue;
        }

        ++planeCount;

        int size;

        if (i == 0)
          size = width * height * 2;
        else if (i < 5)
          size = (width - 1) * (height - 2) * 2;
        else
          size = width * (height - 1) * 2;

        byte[] inflatedBuffer = m_PlaneBuffers[i];

        int deflatedLength = m_DeflatedBuffer.Length;
        ZLibError ce = Compression.Pack(m_DeflatedBuffer, ref deflatedLength, inflatedBuffer, size,
          ZLibQuality.Default);

        if (ce != ZLibError.Okay)
        {
          Console.WriteLine("ZLib error: {0} (#{1})", ce, (int)ce);
          deflatedLength = 0;
          size = 0;
        }

        Write((byte)(0x20 | i));
        Write((byte)size);
        Write((byte)deflatedLength);
        Write((byte)(size >> 4 & 0xF0 | deflatedLength >> 8 & 0xF));
        Write(m_DeflatedBuffer, 0, deflatedLength);

        totalLength += 4 + deflatedLength;
        ArrayPool<byte>.Shared.Return(inflatedBuffer);
      }

      int totalStairBuffersUsed = (totalStairsUsed + (MaxItemsPerStairBuffer - 1)) / MaxItemsPerStairBuffer;

      for (int i = 0; i < totalStairBuffersUsed; ++i)
      {
        ++planeCount;

        int count = totalStairsUsed - i * MaxItemsPerStairBuffer;

        if (count > MaxItemsPerStairBuffer)
          count = MaxItemsPerStairBuffer;

        int size = count * 5;

        byte[] inflatedBuffer = m_StairBuffers[i];

        int deflatedLength = m_DeflatedBuffer.Length;
        ZLibError ce = Compression.Pack(m_DeflatedBuffer, ref deflatedLength, inflatedBuffer, size,
          ZLibQuality.Default);

        if (ce != ZLibError.Okay)
        {
          Console.WriteLine("ZLib error: {0} (#{1})", ce, (int)ce);
          deflatedLength = 0;
          size = 0;
        }

        Write((byte)(9 + i));
        Write((byte)size);
        Write((byte)deflatedLength);
        Write((byte)(size >> 4 & 0xF0 | deflatedLength >> 8 & 0xF));
        Write(m_DeflatedBuffer, 0, deflatedLength);

        totalLength += 4 + deflatedLength;
      }

      for (int i = 0; i < m_StairBuffers.Length; ++i)
        ArrayPool<byte>.Shared.Return(m_StairBuffers[i]);

      ArrayPool<byte>.Shared.Return(m_DeflatedBuffer);

      m_Stream.Seek(15, SeekOrigin.Begin);

      Write((short)totalLength); // Buffer length
      Write((byte)planeCount); // Plane count
    }

    public void Write(int value)
    {
      m_PrimBuffer[0] = (byte)(value >> 24);
      m_PrimBuffer[1] = (byte)(value >> 16);
      m_PrimBuffer[2] = (byte)(value >> 8);
      m_PrimBuffer[3] = (byte)value;

      m_Stream.UnderlyingStream.Write(m_PrimBuffer, 0, 4);
    }

    public void Write(uint value)
    {
      m_PrimBuffer[0] = (byte)(value >> 24);
      m_PrimBuffer[1] = (byte)(value >> 16);
      m_PrimBuffer[2] = (byte)(value >> 8);
      m_PrimBuffer[3] = (byte)value;

      m_Stream.UnderlyingStream.Write(m_PrimBuffer, 0, 4);
    }

    public void Write(short value)
    {
      m_PrimBuffer[0] = (byte)(value >> 8);
      m_PrimBuffer[1] = (byte)value;

      m_Stream.UnderlyingStream.Write(m_PrimBuffer, 0, 2);
    }

    public void Write(byte value)
    {
      m_Stream.UnderlyingStream.WriteByte(value);
    }

    public void Write(byte[] buffer, int offset, int size)
    {
      m_Stream.UnderlyingStream.Write(buffer, offset, size);
    }

    public static void Clear(byte[] buffer, int size)
    {
      for (int i = 0; i < size; ++i)
        buffer[i] = 0;
    }

    public static void ProcessCompression()
    {
      while (!Core.Closing)
      {
        m_Sync.WaitOne();

        int count = m_SendQueue.Count;

        while (count > 0 && m_SendQueue.TryDequeue(out SendQueueEntry sqe))
          try
          {
            Packet p;

            lock (sqe.m_Root)
            {
              p = sqe.m_Root.PacketCache;
            }

            if (p == null)
            {
              p = new DesignStateDetailed(sqe.m_Serial, sqe.m_Revision, sqe.m_xMin, sqe.m_yMin, sqe.m_xMax,
                sqe.m_yMax, sqe.m_Tiles);
              p.SetStatic();

              lock (sqe.m_Root)
              {
                if (sqe.m_Revision == sqe.m_Root.Revision)
                  sqe.m_Root.PacketCache = p;
              }
            }

            sqe.m_NetState.Send(p);
          }
          catch (Exception e)
          {
            Console.WriteLine(e);

            try
            {
              using StreamWriter op = new StreamWriter("dsd_exceptions.txt", true);
              op.WriteLine(e);
            }
            catch
            {
              // ignored
            }
          }
          finally
          {
            count = m_SendQueue.Count;
          }
      }
    }

    public static void SendDetails(NetState ns, HouseFoundation house, DesignState state)
    {
      m_SendQueue.Enqueue(new SendQueueEntry(ns, house, state));

      m_Sync.Set();
    }

    private class SendQueueEntry
    {
      public NetState m_NetState;
      public DesignState m_Root;
      public int m_Revision;
      public uint m_Serial;
      public MultiTileEntry[] m_Tiles;
      public int m_xMin, m_yMin, m_xMax, m_yMax;

      public SendQueueEntry(NetState ns, HouseFoundation foundation, DesignState state)
      {
        m_NetState = ns;
        m_Serial = foundation.Serial;
        m_Revision = state.Revision;
        m_Root = state;

        MultiComponentList mcl = state.Components;

        m_xMin = mcl.Min.X;
        m_yMin = mcl.Min.Y;
        m_xMax = mcl.Max.X;
        m_yMax = mcl.Max.Y;

        m_Tiles = mcl.List;
      }
    }
  }
}