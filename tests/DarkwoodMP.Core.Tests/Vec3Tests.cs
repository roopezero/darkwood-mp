namespace DarkwoodMP.Tests
{
    public class Vec3Tests
    {
        public class Ground
        {
            [Fact]
            public void Takes_the_x_and_z_of_the_game()
            {
                var pos = new Vec3(1f, 2f, 3f);

                var ground = pos.Ground;

                ground.ShouldBe(new Vec2(1f, 3f));
            }
        }
    }
}
