type MessageBannerProps = {
  tone: 'error' | 'success' | 'info';
  message: string;
  inline?: boolean;
};

export function MessageBanner({ tone, message, inline = false }: MessageBannerProps) {
  const classes = ['message-banner', tone, inline ? 'inline-message' : ''].filter(Boolean).join(' ');
  return <div className={classes}>{message}</div>;
}
