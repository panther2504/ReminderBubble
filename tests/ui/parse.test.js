const { parseQuick } = require('../../src/ReminderBubble/wwwroot/js/parse.js');
// Thursday 5 Nov 2026, 14:30 local
const now = new Date(2026, 10, 5, 14, 30, 0);
const f = (d) => d ? `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')} ${String(d.getHours()).padStart(2,'0')}:${String(d.getMinutes()).padStart(2,'0')}` : null;
let fail = 0;
function t(input, title, due, repeat='none') {
  const r = parseQuick(input, now, { dateOrder: 'mdy' });
  const ok = r.title === title && f(r.due) === due && r.repeat === repeat;
  if (!ok) { fail++; console.log('FAIL', JSON.stringify(input), '\n   got ', r.title, '|', f(r.due), '|', r.repeat, '\n   want', title, '|', due, '|', repeat); }
  else console.log('ok  ', JSON.stringify(input).padEnd(46), '->', r.title, '|', f(r.due), '|', r.repeat);
}
t('Call mom tomorrow 5pm', 'Call mom', '2026-11-06 17:00');
t('call mom tomorrow at 5:30 pm', 'Call mom', '2026-11-06 17:30');
t('in 20 min stretch', 'Stretch', '2026-11-05 14:50');
t('Stretch in 2 hours', 'Stretch', '2026-11-05 16:30');
t('take pills in an hour', 'Take pills', '2026-11-05 15:30');
t('Standup every weekday 9am', 'Standup', '2026-11-06 09:00', 'weekdays');
t('Water plants every monday at 8am', 'Water plants', '2026-11-09 08:00', 'weekly');
t('pay rent every month', 'Pay rent', '2026-11-06 09:00', 'monthly');
t('Dentist on friday 3pm', 'Dentist', '2026-11-06 15:00');
t('Dentist next friday 3pm', 'Dentist', '2026-11-06 15:00');
t('Gym thursday 6pm', 'Gym', '2026-11-05 18:00');
t('Review report next monday', 'Review report', '2026-11-09 09:00');
t('Buy gift on dec 12', 'Buy gift', '2026-12-12 09:00');
t('Birthday 12/25 yearly', 'Birthday', '2026-12-25 09:00', 'yearly');
t('Submit tax 3 jan 10am', 'Submit tax', '2027-01-03 10:00');
t('Meeting at 16:00', 'Meeting', '2026-11-05 16:00');
t('Meeting at 9am', 'Meeting', '2026-11-06 09:00');
t('Lunch with Sam at noon tomorrow', 'Lunch with Sam', '2026-11-06 12:00');
t('Call back tonight', 'Call back', '2026-11-05 21:00');
t('Read book this evening', 'Read book', '2026-11-05 18:00');
t('Pick up kids at 5', 'Pick up kids', '2026-11-05 17:00');
t('Wake up tomorrow morning', 'Wake up', '2026-11-06 09:00');
t('remind me to email Anna tomorrow', 'Email Anna', '2026-11-06 09:00');
t('Just a plain title', 'Just a plain title', null);
t('Monitor server logs', 'Monitor server logs', null);
t('Check 3 things', 'Check 3 things', null);
t('submit by friday', 'Submit', '2026-11-06 09:00');
t('daily standup at 10am', 'Standup', '2026-11-06 10:00', 'daily');
console.log(fail ? `\n${fail} FAILED` : '\nall parsed as expected');
process.exit(fail ? 1 : 0);
