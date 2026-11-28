using System.Collections.Generic;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World.Story;

namespace DarkwoodMP.Tests.World.Story
{
    public class PartLayoutTests
    {
        static PartPiece Piece(string name, float x, float z) =>
            new PartPiece { Name = name, At = new Vec2(x, z) };

        public class Moves
        {
            [Fact]
            public void Moves_a_piece_to_the_hosts_spot()
            {
                var ours = new[] { Piece("P1", 0, 0), Piece("P2", 0, 3625) };
                var hosts = new[] { Piece("P1", 0, 0), Piece("P2", 0, -3625) };

                var moves = PartLayout.Moves(ours, hosts);

                moves.Count.ShouldBe(1);
                moves[1].Z.ShouldBe(-3625f);
            }

            [Fact]
            public void Matches_pieces_of_one_name_in_order()
            {
                var ours = new[]
                {
                    Piece("loop", 0, 7600), Piece("loop", 0, 0),
                };

                var hosts = new[]
                {
                    Piece("loop", 0, 7600), Piece("loop", 0, -3975),
                };

                var moves = PartLayout.Moves(ours, hosts);

                moves.Keys.ShouldBe(new[] { 1 });
                moves[1].Z.ShouldBe(-3975f);
            }

            [Fact]
            public void Leaves_pieces_the_host_hasnt_got()
            {
                var ours = new[] { Piece("ours", 5, 5) };

                PartLayout.Moves(ours, new PartPiece[0]).ShouldBeEmpty();
            }

            [Fact]
            public void Leaves_pieces_already_in_place()
            {
                var ours = new[] { Piece("P1", 10, 10) };
                var hosts = new[] { Piece("P1", 10.5f, 10) };

                PartLayout.Moves(ours, hosts).ShouldBeEmpty();
            }
        }

        public class Applies
        {
            [Theory]
            [InlineData(true, "a", "a", true)]
            [InlineData(false, "a", "a", false)]
            [InlineData(true, "a", "b", false)]
            [InlineData(true, null, null, false)]
            public void Applies_only_to_the_part_we_play(
                bool playing,
                string ours,
                string hosts,
                bool applies)
            {
                PartLayout.Applies(playing, ours, hosts).ShouldBe(applies);
            }
        }

        public class Write
        {
            [Fact]
            public void Round_trips_the_pieces()
            {
                var w = new PacketWriter(new RecordingLog());
                w.Begin(MsgType.EpilogueStep);
                PartLayout.Write(
                    w, "epilog_part1a_dream",
                    new List<PartPiece> { Piece("P2", 1, -3625) });
                var r = new PacketReader(w.Buffer, w.Length);

                EpilogueNote note = EpilogueStepMessage.Read(r);
                var pieces = PartLayout.ReadPieces(r);

                note.Step.ShouldBe(EpilogueStep.Layout);
                note.Value.ShouldBe("epilog_part1a_dream");
                pieces.Count.ShouldBe(1);
                pieces[0].Name.ShouldBe("P2");
                pieces[0].At.Z.ShouldBe(-3625f);
            }
        }
    }
}
