# Writing style

How the interface text and generated text should read. It draws on three sources:
- [ASD-STE100](https://www.asd-ste100.org/) (Simplified Technical English), for short, unambiguous instructions.
- [Humanizer](https://github.com/blader/humanizer) and [no-ai-slop](https://github.com/petergyang/no-ai-slop/tree/main/skills/no-ai-slop), for the patterns that make text read as machine-written.

Attorneys read this text between other work. Every sentence should tell them what happened, or what to do next.

## Interface text
- **One idea per sentence.** Keep instructions under about 20 words. Split a sentence joined by a semicolon or a dash.
- **Active voice, with a subject.** "You changed the documents after the last analysis," not "Documents changed."
- **Tell the reader what to do, then why.** "Check it against the original before you use it."
- **Say what the reader gets, not what the software does internally.** Don't write feature lists such as "Reads each document, pulls out quotes, and checks…".
- **Use one word for one thing**, and the words the reader uses:
  - Documents, not records or files.
  - Analyze and analysis, not ingest.
  - The letter, or the demand letter, not the draft narrative.
  - Contradiction and placeholder are fine, since attorneys use them too.
- **No em dashes.** Use a period, a comma, or a middle dot (·) between separate items.
- **Straight quotation marks** (`"`), not curly ones.
- **Never mention** the AI provider, models, tokens or cost.
- **Buttons are verbs** that say what happens: "Analyze", "Draft", "Delete case". After a button press, any confirmation message uses the same verb.

## Generated text
These rules go into the prompts for the document summaries and the case summary. Facts still come only from the quoted documents, so a style rule must never cause a fact to be added, dropped or reworded.

The letter prompt doesn't include them. In testing on `Case_Ellis`, adding them to the letter prompt left more of the letter's sentences without a citation (11 in the original letter, 16 and 27 in two runs with the rules), and grounding matters more than style in a letter sent to an adjuster.

The rules:
- **State facts plainly.** Cut words that inflate or editorialize: crucial, significant, notably, underscores, highlights, testament.
- **Never attribute vaguely.** Where the source matters, as when records disagree, name the document ("The ED record states…"). Never write "experts agree" or "records suggest".
- **No "not X but Y" framing**, and no one-line closing remarks or recap at the end.
- **No em dashes.**
- **Keep sentences short, but keep each fact with its citation.** Splitting a fact into its own sentence leaves that sentence uncited. A lead-in that only announces a topic ("The records differ on X.") is uncited too, so don't write one.
