using Xunit;

namespace JoshOlson.Database.Seeding;

public class IdBlocksTests {
  [Theory]
  [InlineData(0, 1, 1000)]
  [InlineData(1, 1001, 2000)]
  [InlineData(2, 2001, 3000)]
  [InlineData(10, 10_001, 11_000)]
  public void Block_Depth_StartsAndEndsWhereTheAdrSays(int depth, long start, long end) {
    Assert.Equal(start, IdBlocks.Start(depth));
    Assert.Equal(end, IdBlocks.End(depth));
  }

  [Fact]
  public void Block_EveryDepth_IsTheSameSizeAndStartsRightAfterTheOneBefore() {
    for (var depth = 0; depth < 20; depth++) {
      Assert.Equal(IdBlocks.Size, IdBlocks.End(depth) - IdBlocks.Start(depth) + 1);
      Assert.Equal(IdBlocks.End(depth) + 1, IdBlocks.Start(depth + 1));
    }
  }

  [Theory]
  [InlineData(0, 1, true)]
  [InlineData(0, 1000, true)]
  [InlineData(0, 0, false)]
  [InlineData(0, 1001, false)]
  [InlineData(1, 1000, false)]
  [InlineData(1, 1001, true)]
  [InlineData(1, 2000, true)]
  [InlineData(1, 2001, false)]
  [InlineData(2, 2001, true)]
  public void Contains_Id_IsInTheDepthsBlockOnly(int depth, long id, bool expected)
    => Assert.Equal(expected, IdBlocks.Contains(depth, id));
}
