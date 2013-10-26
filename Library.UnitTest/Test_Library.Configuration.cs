using System.Collections.Generic;
using System.IO;
using Library.Configuration;
using NUnit.Framework;

namespace Library.UnitTest
{
    [TestFixture, Category("Library.Configuration")]
    public class Test_Library_Configuration
    {
        [Test]
        public void Test_SettingsBase()
        {
            string directoryPath = "Test_SettingsBase";

            TestSettings testSettings = new TestSettings();
            testSettings.Text = "test";
            testSettings.Save(directoryPath);
            testSettings.Text = "";
            testSettings.Load(directoryPath);

            Assert.AreEqual(testSettings.Text, "test", "SettingsBase");

            Directory.Delete("Test_SettingsBase", true);
        }

        public class TestSettings : Library.Configuration.SettingsBase, IThisLock
        {
            private object _thisLock = new object();

            public TestSettings()
                : base(new List<ISettingsContext>() 
                { 
                    new SettingsContext<string>() { Name = "Text", Value = "" },
                })
            {

            }

            public override void Load(string directoryPath)
            {
                lock (this.ThisLock)
                {
                    base.Load(directoryPath);
                }
            }

            public override void Save(string directoryPath)
            {
                lock (this.ThisLock)
                {
                    base.Save(directoryPath);
                }
            }

            public string Text
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return (string)this["Text"];
                    }
                }

                set
                {
                    lock (this.ThisLock)
                    {
                        this["Text"] = value;
                    }
                }
            }

            #region IThisLock メンバ

            public object ThisLock
            {
                get
                {
                    return _thisLock;
                }
            }

            #endregion
        }
    }
}
