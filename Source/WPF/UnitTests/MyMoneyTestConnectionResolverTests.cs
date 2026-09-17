using System;
using NUnit.Framework;
using Walkabout.Data;
using Walkabout.TestSupport;

namespace Walkabout.Tests
{
    public class MyMoneyTestConnectionResolverTests
    {
        [Test]
        public void BuildConnectionString_ValidConfigAndCredential_ProducesExpectedConnectionString()
        {
            var config = new DataEngineConfig { Server = "Redmond", Database = "MyMoney" };
            var credential = new DataEngineCredential { UserId = "MyMoneyTest", Password = "secret" };

            string connectionString = MyMoneyTestConnectionResolver.BuildConnectionString(config, credential);

            Assert.That(connectionString, Is.EqualTo(
                "Data Source=Redmond;Initial Catalog=MyMoney;User ID=MyMoneyTest;Password=secret;TrustServerCertificate=True"));
        }

        [Test]
        public void BuildConnectionString_NullConfig_ThrowsArgumentNullException()
        {
            var credential = new DataEngineCredential { UserId = "MyMoneyTest", Password = "secret" };
            Assert.Throws<ArgumentNullException>(() => MyMoneyTestConnectionResolver.BuildConnectionString(null, credential));
        }

        [Test]
        public void BuildConnectionString_NullCredential_ThrowsArgumentNullException()
        {
            var config = new DataEngineConfig { Server = "Redmond", Database = "MyMoney" };
            Assert.Throws<ArgumentNullException>(() => MyMoneyTestConnectionResolver.BuildConnectionString(config, null));
        }

        [Test]
        public void BuildConnectionString_ConfigMissingServer_ThrowsInvalidOperationException()
        {
            var config = new DataEngineConfig { Server = null, Database = "MyMoney" };
            var credential = new DataEngineCredential { UserId = "MyMoneyTest", Password = "secret" };
            Assert.Throws<InvalidOperationException>(() => MyMoneyTestConnectionResolver.BuildConnectionString(config, credential));
        }

        [Test]
        public void BuildConnectionString_ConfigMissingDatabase_ThrowsInvalidOperationException()
        {
            var config = new DataEngineConfig { Server = "Redmond", Database = null };
            var credential = new DataEngineCredential { UserId = "MyMoneyTest", Password = "secret" };
            Assert.Throws<InvalidOperationException>(() => MyMoneyTestConnectionResolver.BuildConnectionString(config, credential));
        }

        [Test]
        public void ValidateMatchesAdminConnection_SameServerAndDatabase_DoesNotThrow()
        {
            var config = new DataEngineConfig { Server = "Redmond", Database = "MyMoney" };
            string adminConnectionString = "Data Source=Redmond;Initial Catalog=MyMoney;User ID=MyMoneyAdmin;Password=x;TrustServerCertificate=True";

            Assert.DoesNotThrow(() => MyMoneyTestConnectionResolver.ValidateMatchesAdminConnection(adminConnectionString, config));
        }

        [Test]
        public void ValidateMatchesAdminConnection_SameServerAndDatabase_CaseInsensitive_DoesNotThrow()
        {
            var config = new DataEngineConfig { Server = "REDMOND", Database = "mymoney" };
            string adminConnectionString = "Data Source=Redmond;Initial Catalog=MyMoney;User ID=MyMoneyAdmin;Password=x;TrustServerCertificate=True";

            Assert.DoesNotThrow(() => MyMoneyTestConnectionResolver.ValidateMatchesAdminConnection(adminConnectionString, config));
        }

        [Test]
        public void ValidateMatchesAdminConnection_DifferentServer_ThrowsInvalidOperationException()
        {
            var config = new DataEngineConfig { Server = "SomeOtherServer", Database = "MyMoney" };
            string adminConnectionString = "Data Source=Redmond;Initial Catalog=MyMoney;User ID=MyMoneyAdmin;Password=x;TrustServerCertificate=True";

            var ex = Assert.Throws<InvalidOperationException>(
                () => MyMoneyTestConnectionResolver.ValidateMatchesAdminConnection(adminConnectionString, config));
            Assert.That(ex.Message, Does.Contain("SomeOtherServer"));
            Assert.That(ex.Message, Does.Contain("Redmond"));
        }

        [Test]
        public void ValidateMatchesAdminConnection_DifferentDatabase_ThrowsInvalidOperationException()
        {
            var config = new DataEngineConfig { Server = "Redmond", Database = "SomeOtherDatabase" };
            string adminConnectionString = "Data Source=Redmond;Initial Catalog=MyMoney;User ID=MyMoneyAdmin;Password=x;TrustServerCertificate=True";

            var ex = Assert.Throws<InvalidOperationException>(
                () => MyMoneyTestConnectionResolver.ValidateMatchesAdminConnection(adminConnectionString, config));
            Assert.That(ex.Message, Does.Contain("SomeOtherDatabase"));
            Assert.That(ex.Message, Does.Contain("MyMoney"));
        }
    }
}
