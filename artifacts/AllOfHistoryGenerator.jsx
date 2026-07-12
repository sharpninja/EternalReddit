import { useState } from "react";

const FONT_LINK = "https://fonts.googleapis.com/css2?family=IBM+Plex+Mono:ital,wght@0,400;0,500;0,600;0,700;1,400&family=Fraunces:ital,wght@1,600&display=swap";

const SYSTEM_PROMPT = `You write fictional, satirical Reddit threads set on "r/AllOfHistory" — a subreddit where historical, legendary, and mythical figures post as contemporaries, all mixed into one timeline. This is a comedy writing exercise, not a factual record.

Rules:
- Never write content involving any real, named public figure whose primary legacy involves atrocity, violent oppression, or founding a hate group (e.g. no jokes built on genocide, slavery, terrorism, or similar). If the user's prompt points toward that, redirect the thread toward a related but safe angle and pick different figures.
- Keep humor affectionate, not mean-spirited or bigoted, and avoid stereotypes.
- Ground jokes in each figure's real, documented personality, achievements, or well-known quirks — specific and character-true, not generic.
- Do not put real, fabricated quotes in a figure's mouth that could be mistaken for something they actually said/wrote historically — keep it clearly framed as satire.
- Reply ONLY with a single JSON object, no markdown fences, no preamble, matching this exact shape:
{
  "flair": "short category label, e.g. AITA, Unpopular Opinion, LPT, Advice, Update, Shower Thought, Vent",
  "username": "u/handle_in_snake_case",
  "title": "post title, punchy, in character",
  "body": "1-4 sentences of post body text",
  "votes": "number like 41.2K",
  "comments": [
    {
      "username": "u/handle_in_snake_case",
      "flair": "optional short bio tag or empty string",
      "text": "comment text, in character, ideally an unexpected but on-point crossover",
      "votes": "number like 6.1K",
      "replies": [
        { "username": "u/handle", "flair": "", "text": "reply text", "votes": "1.8K" }
      ]
    }
  ]
}
Include 3-5 comments, at least one with a nested reply. Make it genuinely funny and specific.`;

function StyleBlock() {
  return (
    <style>{`
      .aoh-root {
        background:#0b0c0a;
        color:#e9e4d4;
        font-family:'IBM Plex Mono', monospace;
        min-height:100vh;
        background-image:repeating-linear-gradient(0deg, rgba(255,180,84,0.012) 0px, rgba(255,180,84,0.012) 1px, transparent 1px, transparent 3px);
      }
      .aoh-wrap{ max-width:680px; margin:0 auto; padding-bottom:60px; }
      .aoh-header{ padding:22px 20px 16px; border-bottom:1px solid #2b2c22; }
      .aoh-sub{ font-family:'Fraunces', serif; font-style:italic; font-weight:600; font-size:1.7rem; color:#ffb454; }
      .aoh-desc{ color:#8c8a77; font-size:0.82rem; margin-top:4px; }
      .aoh-composer{ margin:16px 20px; padding:14px 16px; border:1px solid #2b2c22; background:#141511; border-radius:2px; }
      .aoh-label{ font-size:0.68rem; letter-spacing:0.12em; text-transform:uppercase; color:#8a6a35; margin-bottom:8px; }
      .aoh-inputrow{ display:flex; gap:8px; }
      .aoh-input{
        flex:1; background:#0b0c0a; border:1px solid #2b2c22; color:#e9e4d4;
        font-family:'IBM Plex Mono', monospace; font-size:0.88rem; padding:9px 10px; border-radius:2px;
      }
      .aoh-input:focus{ outline:none; border-color:#ffb454; }
      .aoh-btn{
        background:#ffb454; color:#0b0c0a; border:none; font-weight:700;
        font-family:'IBM Plex Mono', monospace; font-size:0.82rem; padding:0 16px; border-radius:2px; cursor:pointer;
      }
      .aoh-btn:disabled{ opacity:0.5; cursor:default; }
      .aoh-btn:hover:not(:disabled){ background:#ffc57a; }
      .aoh-hint{ color:#8c8a77; font-size:0.7rem; margin-top:8px; }
      .aoh-error{ color:#ff7a7a; font-size:0.78rem; margin-top:10px; }

      .aoh-post{ border-bottom:1px solid #2b2c22; padding:16px 20px; display:flex; gap:12px; }
      .aoh-votes{ flex:0 0 auto; width:34px; text-align:center; padding-top:2px; }
      .aoh-arrow{ color:#8c8a77; font-size:0.9rem; line-height:1; }
      .aoh-arrow.up{ color:#ff7a45; }
      .aoh-n{ font-size:0.8rem; font-weight:600; color:#ffb454; margin:3px 0; }
      .aoh-col{ flex:1; min-width:0; }
      .aoh-meta{ font-size:0.72rem; color:#8c8a77; margin-bottom:4px; }
      .aoh-meta b{ color:#e9e4d4; }
      .aoh-flair{
        display:inline-block; background:#8a6a35; color:#0b0c0a; font-size:0.62rem; font-weight:700;
        padding:1px 6px; border-radius:3px; margin-right:6px; text-transform:uppercase; letter-spacing:0.04em;
      }
      .aoh-title{ font-weight:600; font-size:1.02rem; margin-bottom:6px; line-height:1.35; }
      .aoh-body{ font-size:0.9rem; line-height:1.5; }
      .aoh-stats{ margin-top:10px; font-size:0.74rem; color:#8c8a77; display:flex; gap:16px; }

      .aoh-comments{ margin-top:14px; padding-left:14px; border-left:1px solid #2b2c22; }
      .aoh-comment{ margin-top:12px; display:flex; gap:8px; }
      .aoh-comment .aoh-votes{ width:26px; }
      .aoh-comment .aoh-n{ font-size:0.68rem; }
      .aoh-cuser{ font-size:0.76rem; color:#ffb454; font-weight:600; }
      .aoh-cflair{ color:#8c8a77; font-weight:400; font-size:0.68rem; }
      .aoh-ctext{ font-size:0.85rem; line-height:1.45; margin-top:2px; }
      .aoh-creply{ margin-left:34px; padding-left:12px; border-left:1px solid #2b2c22; }
      .aoh-cactions{ font-size:0.66rem; color:#8c8a77; margin-top:3px; }

      .aoh-empty{ padding:40px 20px; color:#8c8a77; font-size:0.85rem; text-align:center; }
    `}</style>
  );
}

function Comment({ c }) {
  return (
    <div className="aoh-comment">
      <div className="aoh-votes">
        <div className="aoh-arrow up">▲</div>
        <div className="aoh-n">{c.votes}</div>
      </div>
      <div>
        <div className="aoh-cuser">
          {c.username} {c.flair ? <span className="aoh-cflair">· {c.flair}</span> : null}
        </div>
        <div className="aoh-ctext">{c.text}</div>
        <div className="aoh-cactions">reply · award · report</div>
        {(c.replies || []).map((r, i) => (
          <div className="aoh-creply" key={i}>
            <div className="aoh-comment">
              <div className="aoh-votes">
                <div className="aoh-arrow up">▲</div>
                <div className="aoh-n">{r.votes}</div>
              </div>
              <div>
                <div className="aoh-cuser">
                  {r.username} {r.flair ? <span className="aoh-cflair">· {r.flair}</span> : null}
                </div>
                <div className="aoh-ctext">{r.text}</div>
                <div className="aoh-cactions">reply · report</div>
              </div>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

function Post({ thread }) {
  return (
    <div className="aoh-post">
      <div className="aoh-votes">
        <div className="aoh-arrow up">▲</div>
        <div className="aoh-n">{thread.votes}</div>
        <div className="aoh-arrow">▼</div>
      </div>
      <div className="aoh-col">
        <div className="aoh-meta">
          <span className="aoh-flair">{thread.flair}</span>
          <b>{thread.username}</b>
        </div>
        <div className="aoh-title">{thread.title}</div>
        <div className="aoh-body">{thread.body}</div>
        <div className="aoh-stats">
          <span>💬 {(thread.comments || []).length} comments</span>
          <span>🏅 {Math.max(1, Math.floor(Math.random() * 12))} awards</span>
          <span>share</span>
        </div>
        <div className="aoh-comments">
          {(thread.comments || []).map((c, i) => (
            <Comment c={c} key={i} />
          ))}
        </div>
      </div>
    </div>
  );
}

export default function AllOfHistoryGenerator() {
  const [prompt, setPrompt] = useState("");
  const [threads, setThreads] = useState([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");

  async function generate() {
    if (!prompt.trim() || loading) return;
    setLoading(true);
    setError("");
    try {
      const response = await fetch("https://api.anthropic.com/v1/messages", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          model: "claude-sonnet-4-6",
          max_tokens: 1000,
          system: SYSTEM_PROMPT,
          messages: [
            { role: "user", content: `Prompt topic for the thread: ${prompt.trim()}` }
          ]
        })
      });
      const data = await response.json();
      const textBlock = (data.content || []).find((b) => b.type === "text");
      if (!textBlock) throw new Error("No response text returned.");
      const clean = textBlock.text.replace(/```json|```/g, "").trim();
      const parsed = JSON.parse(clean);
      setThreads((prev) => [parsed, ...prev]);
      setPrompt("");
    } catch (e) {
      setError("Couldn't materialize that thread. Try a different prompt.");
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="aoh-root">
      <StyleBlock />
      <div className="aoh-wrap">
        <div className="aoh-header">
          <div className="aoh-sub">r/AllOfHistory</div>
          <div className="aoh-desc">
            submit a prompt, get a thread written by whoever from history shows up for it
          </div>
        </div>

        <div className="aoh-composer">
          <div className="aoh-label">New Thread Prompt</div>
          <div className="aoh-inputrow">
            <input
              className="aoh-input"
              type="text"
              placeholder="e.g. best productivity hacks, worst roommate story, unpopular fashion opinions..."
              value={prompt}
              onChange={(e) => setPrompt(e.target.value)}
              onKeyDown={(e) => e.key === "Enter" && generate()}
              disabled={loading}
            />
            <button className="aoh-btn" onClick={generate} disabled={loading}>
              {loading ? "..." : "Post"}
            </button>
          </div>
          <div className="aoh-hint">
            AI generates a fictional, satirical thread with historical figures reacting — new content each time.
          </div>
          {error && <div className="aoh-error">{error}</div>}
        </div>

        {threads.length === 0 && !loading && (
          <div className="aoh-empty">no threads yet — submit a prompt above to start one</div>
        )}

        {threads.map((t, i) => (
          <Post thread={t} key={i} />
        ))}
      </div>
    </div>
  );
}
