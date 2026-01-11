import "@/styles/globals.css";

import { type Metadata } from "next";
import { Geist, Major_Mono_Display } from "next/font/google";

import { TRPCReactProvider } from "@/trpc/react";

export const metadata: Metadata = {
  title: "Lexicons - ToyLang",
  description: "Project O, Lexicons, C# hand-written parser",
  icons: [{ rel: "icon", url: "/favicon.ico" }],
};

const geist = Geist({
  subsets: ["latin"],
  variable: "--font-geist-sans",
});

const majorMonoDisplay = Major_Mono_Display({
  subsets: ["latin"],
  weight: "400",
  variable: "--font-major-mono-display",
});

export default function RootLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  return (
    <html
      lang="en"
      className={`${geist.variable} ${majorMonoDisplay.variable} overscroll-none`}
    >
      <body className="bg-[#111]">
        <TRPCReactProvider>{children}</TRPCReactProvider>
      </body>
    </html>
  );
}
