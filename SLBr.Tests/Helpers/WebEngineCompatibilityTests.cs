using SLBr.WebView;

namespace SLBr.Tests.Helpers
{
    public class WebEngineCompatibilityTests
    {
        [Theory]
        [InlineData(1, WebEngineType.ChromiumEdge)]
        [InlineData(2, WebEngineType.Trident)]
        public void SavedEngineIds_KeepTheirOriginalMeaning(int SavedId, WebEngineType Expected)
        {
            Assert.Equal(Expected, (WebEngineType)SavedId);
            Assert.True(Enum.IsDefined(typeof(WebEngineType), SavedId));
        }

        [Theory]
        [InlineData(0)] // Removed CEF engine must be migrated, never interpreted as Edge or IE.
        [InlineData(-1)]
        [InlineData(3)]
        public void RemovedOrInvalidEngineIds_RequireMigration(int SavedId)
        {
            Assert.False(Enum.IsDefined(typeof(WebEngineType), SavedId));
        }
    }
}
