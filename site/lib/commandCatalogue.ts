import source from '../../docs/COMMANDS.md?raw';

export interface CommandEntry {
  name: string;
  group: string;
  aliases: string;
  description: string;
}

const declaredCount = Number(source.match(/Total commands: \*\*(\d+)\*\*/)?.[1]);
const index = source.match(/## Index\r?\n[\s\S]*?(?=\r?\n## )/)?.[0] ?? '';

export const commands = index
  .split(/\r?\n/)
  .filter((line) => /^\| \[`[^`]+`\]\(#[^)]+\) \|/.test(line))
  .map((line): CommandEntry => {
    const [, commandCell, groupCell, aliasesCell, description] = line
      .split('|')
      .map((cell) => cell.trim());

    return {
      name: commandCell.match(/\[`([^`]+)`\]/)?.[1] ?? '',
      group: groupCell.replaceAll('`', ''),
      aliases: aliasesCell.replaceAll('`', ''),
      description,
    };
  });

if (!Number.isFinite(declaredCount) || commands.length !== declaredCount) {
  throw new Error(
    `Generated command catalogue is incomplete: expected ${declaredCount}, parsed ${commands.length}.`,
  );
}
