using System;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    // Only raised after the operation's transaction group has verifiably rolled back.
    internal sealed class SleeveOperationRolledBackException : Exception
    {
        internal SleeveOperationRolledBackException(Exception cause)
            : base("套管建立／更新未完成，該次變更已回復；先前成功載入的族群保留。\n" + cause.Message, cause) { }
    }
}
