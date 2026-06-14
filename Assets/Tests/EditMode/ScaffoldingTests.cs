using NUnit.Framework;

namespace KakigoriTD.Tests.EditMode
{
    /// <summary>
    /// Unit 0 スモークテスト: EditMode テストアセンブリがコンパイルされ、
    /// テストパイプライン（asmdef 配線・tests-run）が機能していることを確認する。
    /// </summary>
    public class ScaffoldingTests
    {
        [Test]
        public void EditModeAssembly_IsWired_Passes()
        {
            Assert.Pass("KakigoriTD.Tests.EditMode compiles and the test pipeline is wired.");
        }
    }
}
