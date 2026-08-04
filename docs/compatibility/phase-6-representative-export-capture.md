# Phase 6 representative Streamer.bot export capture

## Why this capture is required

The retained Phase 1 exports prove the action, trigger, Set Argument, Execute C#
Code, and outer `SBAE` envelope shapes. Their `commands` and `queues`
collections are empty. Foundry must not invent version-sensitive fields or
silently omit designer items, so a populated representative export is required
before the stable package adapter is finalized.

The inspected Execute C# Code `byteCode` value is Base64-encoded C# source, not
an opaque compiled assembly. No additional capture is required for that field.

## Create in each disposable instance

Repeat this recipe in Streamer.bot 1.0.4 stable, 1.0.5 alpha, and 1.0.5 beta.
Use the same names and settings in all three.

1. Create an action queue named `Foundry Phase 6 Queue`.
2. Create an enabled action named `Foundry Phase 6 Representative`.
3. Assign the action to `Foundry Phase 6 Queue`.
4. Add a **Set Argument** sub-action:
   - Variable: `foundryPhase6Input`
   - Value: `representative`
5. Add an **Execute C# Code** sub-action with:

   ```csharp
   public class CPHInline
   {
       public bool Execute()
       {
           CPH.LogInfo("Foundry Phase 6 representative");
           return true;
       }
   }
   ```

6. Create an enabled command named `!foundryphase6`.
7. If the command editor supports aliases, add `!ffphase6`.
8. Set a global cooldown of 5 seconds and a user cooldown of 10 seconds.
9. Add the command as a trigger for `Foundry Phase 6 Representative`.
10. Retain the existing **Test** trigger as a second trigger.

## Export

Open Streamer.bot's Import/Export window and create one export containing:

- `Foundry Phase 6 Representative`;
- `Foundry Phase 6 Queue`;
- `!foundryphase6`;
- both triggers and both sub-actions.

Use this package metadata:

- Name: `Creators Forge Foundry Phase 6 Representative`
- Author: `Creators Forge Foundry`
- Version: `0.1.0`
- Description: `Representative Phase 6 action, command, queue, triggers, and code`

Save the complete import code without decoding or editing it:

```text
experiments/StreamerBotCompatibility/captures/raw/phase6/
  stable-1.0.4
  alpha-1.0.5-alpha.34
  beta-1.0.5-beta.1
```

The `raw` tree is intentionally ignored by Git. Do not paste credentials,
tokens, production channel data, or unrelated actions into the representative
package.

## Acceptance check

For each saved file, confirm that it:

- imports into the same disposable instance;
- includes the action, command, and queue after import;
- retains the command and Test triggers;
- retains both sub-actions;
- contains no production secrets.

After capture, Foundry will decode and normalize the files, document the
version differences, implement the stable adapter, decode its own output, and
compare the meaningful action/trigger/command/queue model.
