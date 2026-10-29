namespace MoveItAutomationUI.Models;

// Initial selections/order match the supplied MOVEit New SFTP Host screenshots.
// Available choices always come from MOVEit's discovery APIs.
public sealed class SshAlgorithmOptions
{
    public const string SectionName = "SshAlgorithms";
    public const string InitialEncryption = "aes256-ctr,aes192-ctr,aes128-ctr,aes256-gcm@openssh.com,aes128-gcm@openssh.com,chacha20-poly1305@openssh.com";
    public const string InitialKeyExchange = "curve25519-sha256,curve25519-sha256@libssh.org,diffie-hellman-group-exchange-sha256,diffie-hellman-group14-sha256,diffie-hellman-group16-sha512,diffie-hellman-group18-sha512,ecdh-sha2-nistp256,ecdh-sha2-nistp384,ecdh-sha2-nistp521,gss-group14-sha256-toWM5Slw5Ew8Mqkay+al2g==,gss-group16-sha512-toWM5Slw5Ew8Mqkay+al2g==,gss-nistp256-sha256-toWM5Slw5Ew8Mqkay+al2g==,gss-curve25519-sha256-toWM5Slw5Ew8Mqkay+al2g==";
    public SettingDefinition<string> Encryption { get; set; } = new()
    {
        Name = "Default Encryption Algorithms", Value = InitialEncryption, Type = "String",
        Description = "Ordered default encryption algorithms for new SFTP Hosts and the Host editor Reset action."
    };
    public SettingDefinition<string> KeyExchange { get; set; } = new()
    {
        Name = "Default KEX Algorithms", Value = InitialKeyExchange, Type = "String",
        Description = "Ordered default key exchange algorithms for new SFTP Hosts and the Host editor Reset action."
    };
}
