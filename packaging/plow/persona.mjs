/** Git archives preserve committed CRLF bytes; the build must accept either checkout style. */
export function connectPlowPersona(source) {
  const prompt = source.replace(/\r\n/g, '\n');
  const local = 'The local cockpit is the current connection;\nPlow Chat is a later hosted option.';
  if (prompt.split(local).length !== 2)
    throw new Error('Marketing persona changed; review its Plow connection instructions.');
  return prompt.replace(local,
    'Plow Chat and the HireZero cockpit are two entrances to this same employee and work ledger.');
}

/** The image's prompt: the employee persona connected to Plow, then the hosted and working-by-text instructions. */
export function hostedPersona(source, hosted) {
  return connectPlowPersona(source) + '\n' + hosted.replace(/\r\n/g, '\n');
}
