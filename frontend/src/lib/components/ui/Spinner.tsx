import { useEffect, useState } from "react";

const CHARS = ["⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇"];

export function Spinner() {
  const [index, setIndex] = useState(0);

  useEffect(() => {
    const interval = setInterval(() => {
      setIndex((prevIndex) => (prevIndex + 1) % CHARS.length);
    }, 50);

    return () => clearInterval(interval);
  }, []);

  return <span>{CHARS[index]}</span>;
}
