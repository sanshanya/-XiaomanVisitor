using System;

namespace XiaomanVisitor
{
    public static class SupportPhoneDialog
    {
        // Native telephone choices require a non-null choice item. Their visible
        // label comes from the PLAYER dialogue, not AddChoice's item-name argument.
        public static Dialogue Choices(Func<string,string,Dialogue> line,Func<GameItem> item,Action apply,Action end)
        {
            var prompt=line(null,"xm_w04_phone_02");
            var confirmation=line(null,"xm_w04_phone_confirm").SetEndAction(end);
            var yes=line("PLAYER","ui_w04_phone_agree").SetNextDialogue(confirmation);
            var later=line("PLAYER","ui_w04_phone_later").SetEndAction(end);
            prompt.AddChoice(item(),"","",yes,apply);
            prompt.AddChoice(item(),"","",later);
            return prompt;
        }
    }
}
