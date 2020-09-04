namespace Server.Items
{
    public class LargeBedSouthAddon : BaseAddon
    {
        [Constructible]
        public LargeBedSouthAddon()
        {
            AddComponent(new AddonComponent(0xA83), 0, 0, 0);
            AddComponent(new AddonComponent(0xA7F), 0, 1, 0);
            AddComponent(new AddonComponent(0xA82), 1, 0, 0);
            AddComponent(new AddonComponent(0xA7E), 1, 1, 0);
        }

        public LargeBedSouthAddon(Serial serial) : base(serial)
        {
        }

        public override BaseAddonDeed Deed => new LargeBedSouthDeed();

        public override void Serialize(IGenericWriter writer)
        {
            base.Serialize(writer);

            writer.Write(0); // version
        }

        public override void Deserialize(IGenericReader reader)
        {
            base.Deserialize(reader);

            var version = reader.ReadInt();
        }
    }

    public class LargeBedSouthDeed : BaseAddonDeed
    {
        [Constructible]
        public LargeBedSouthDeed()
        {
        }

        public LargeBedSouthDeed(Serial serial) : base(serial)
        {
        }

        public override BaseAddon Addon => new LargeBedSouthAddon();
        public override int LabelNumber => 1044323; // large bed (south)

        public override void Serialize(IGenericWriter writer)
        {
            base.Serialize(writer);

            writer.Write(0); // version
        }

        public override void Deserialize(IGenericReader reader)
        {
            base.Deserialize(reader);

            var version = reader.ReadInt();
        }
    }
}