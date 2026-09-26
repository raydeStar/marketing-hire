/** A draft as it would be posted. Drafts written before the host cleaned them can start with "[Image text: …]" (the image it
 * asked for) or a line naming the channel ("X"); neither is part of the post. */
export function draftText(draft:{channel:string;content:string}):string{
  let text=draft.content.replace(/^\s*\[(?:image|image text|visual|graphic)\s*:[^\]\n]*\]\s*\n*/gim,'');
  const [first,...rest]=text.split('\n');
  const label=first.trim().replace(/[*#:]/g,'').trim().toLowerCase();
  if(rest.length&&label&&[draft.channel.toLowerCase(),draft.channel.toLowerCase()+' post'].includes(label))text=rest.join('\n').replace(/^\s*\n/,'');
  return text;
}

/** Markdown joins single lines into one paragraph; an email's "Subject:" and "Preview text:" lines stay on their own lines. */
export function keepLineBreaks(markdown:string):string{
  const lines=markdown.split('\n');
  const block=/^\s*([-*+]\s|\d+[.)]\s|#|>|\||```)/;
  return lines.map((line,index)=>{
    const next=lines[index+1];
    return line.trim()&&next?.trim()&&!block.test(line)&&!block.test(next)&&!line.endsWith('  ')?line+'  ':line;
  }).join('\n');
}
