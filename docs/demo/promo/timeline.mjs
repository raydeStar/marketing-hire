// Shared clock for the promo: the music and the picture read the same bars, so every cut, click and slam lands on a beat.
export const BPM=120;
export const BEAT=60/BPM;          // 0.5 s
export const BAR=4*BEAT;           // 2 s
export const BARS=34;
export const DURATION=BARS*BAR;    // 68 s
export const FPS=30;

// Sections in bars. Picture scenes and music arrangement both key off these.
export const SECTIONS={
  cold:      [0,4],    // kinetic open, no drums; riser into bar 4
  reveal:    [4,6],    // drop 1: the mark slams in
  brief:     [6,10],
  shift:     [10,14],
  results:   [14,19],
  breakdown: [19,22],  // drums out: revision + lesson; riser into bar 22
  approve:   [22,26],  // drop 2: Approve all
  anywhere:  [26,30],
  outro:     [30,34],
};
export const at=bar=>bar*BAR;

// Cold-open hits (seconds): one sub boom per line, then a stutter on beats into the drop.
export const COLD_HITS=[0.5,2.5,4.5];
export const COLD_STUTTER=[6,6.5,7,7.5];

// UI moments the music punctuates: cursor clicks, the eight shift phases, the seven prepared pieces, the seven approvals.
export const CLICKS=[at(10)+1,at(22)];
export const CHIP_TIMES=Array.from({length:8},(_,i)=>at(10)+1.5+i*BEAT);
export const RESULT_TIMES=Array.from({length:7},(_,i)=>at(14)+0.5+i*BEAT);
export const APPROVE_TIMES=Array.from({length:7},(_,i)=>at(22)+0.5+i*BEAT);
