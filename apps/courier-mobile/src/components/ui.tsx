import { colors, tones, type Tone } from '@/theme/colors';
import type { PropsWithChildren, ReactNode } from 'react';
import { ActivityIndicator, Pressable, ScrollView, StyleSheet, Text, View, type StyleProp, type ViewStyle } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

export function Screen({ children, scroll = true }: PropsWithChildren<{ scroll?: boolean }>) {
  return <SafeAreaView style={styles.safe} edges={['top']}>
    {scroll ? <ScrollView contentContainerStyle={styles.screen} showsVerticalScrollIndicator={false}>{children}</ScrollView> : <View style={[styles.screen, styles.fixed]}>{children}</View>}
  </SafeAreaView>;
}

export function PageHeader({ eyebrow, title, description, right }: { eyebrow?: string; title: string; description?: string; right?: ReactNode }) {
  return <View style={styles.header}>
    <View style={styles.headerCopy}>
      {eyebrow ? <Text style={styles.eyebrow}>{eyebrow}</Text> : null}
      <Text style={styles.title}>{title}</Text>
      {description ? <Text style={styles.description}>{description}</Text> : null}
    </View>
    {right}
  </View>;
}

export function Card({ children, style }: PropsWithChildren<{ style?: StyleProp<ViewStyle> }>) {
  return <View style={[styles.card, style]}>{children}</View>;
}

export function Pill({ label, tone = 'neutral', dot = true }: { label: string; tone?: Tone; dot?: boolean }) {
  const palette = tones[tone];
  return <View style={[styles.pill, { backgroundColor: palette.bg }]}>
    {dot ? <View style={[styles.pillDot, { backgroundColor: palette.fg }]} /> : null}
    <Text style={[styles.pillText, { color: palette.fg }]}>{label}</Text>
  </View>;
}

export function Button({ title, onPress, loading, disabled, variant = 'primary', icon, size = 'md' }: {
  title: string; onPress(): void; loading?: boolean; disabled?: boolean;
  variant?: 'primary' | 'secondary' | 'danger' | 'ghost' | 'ink'; icon?: ReactNode; size?: 'md' | 'lg';
}) {
  const light = variant === 'primary' || variant === 'danger' || variant === 'ink';
  return <Pressable accessibilityRole="button" accessibilityLabel={title} disabled={disabled || loading} onPress={onPress}
    style={({ pressed }) => [styles.button, size === 'lg' && styles.buttonLarge, styles[`button_${variant}`], pressed && styles.pressed, (disabled || loading) && styles.disabled]}>
    {loading ? <ActivityIndicator color={light ? colors.white : colors.primary} /> : icon}
    <Text style={[styles.buttonText, size === 'lg' && styles.buttonTextLarge, styles[`buttonText_${variant}`]]}>{title}</Text>
  </Pressable>;
}

export function IconBadge({ children, tone = 'primary', size = 40 }: PropsWithChildren<{ tone?: Tone; size?: number }>) {
  return <View style={[styles.iconBadge, { width: size, height: size, borderRadius: size * 0.28, backgroundColor: tones[tone].bg }]}>{children}</View>;
}

export function EmptyState({ title, description, icon }: { title: string; description: string; icon?: ReactNode }) {
  return <View style={styles.empty}>
    {icon ? <View style={styles.emptyIcon}>{icon}</View> : null}
    <Text style={styles.emptyTitle}>{title}</Text>
    <Text style={styles.emptyDescription}>{description}</Text>
  </View>;
}

export function LoadingScreen() {
  return <View style={styles.loading}><ActivityIndicator size="large" color={colors.primary} /></View>;
}

const styles = StyleSheet.create({
  safe: { flex: 1, backgroundColor: colors.background },
  screen: { paddingHorizontal: 16, paddingTop: 12, paddingBottom: 120, gap: 12, flexGrow: 1 },
  fixed: { flex: 1, paddingBottom: 0 },
  header: { flexDirection: 'row', alignItems: 'flex-end', gap: 12, marginBottom: 6, paddingHorizontal: 2 },
  headerCopy: { flex: 1, gap: 4 },
  eyebrow: { color: colors.primaryDark, fontSize: 11, fontWeight: '700', letterSpacing: 1.2, textTransform: 'uppercase' },
  title: { color: colors.text, fontSize: 28, lineHeight: 34, fontWeight: '700', letterSpacing: -0.8 },
  description: { color: colors.textMuted, fontSize: 14, lineHeight: 20 },
  card: { backgroundColor: colors.surface, borderRadius: 18, borderWidth: StyleSheet.hairlineWidth, borderColor: colors.borderStrong, padding: 16, gap: 12, shadowColor: colors.shadow, shadowOpacity: 0.04, shadowRadius: 8, shadowOffset: { width: 0, height: 2 }, elevation: 1 },
  pill: { alignSelf: 'flex-start', flexDirection: 'row', alignItems: 'center', gap: 6, paddingHorizontal: 9, paddingVertical: 4, borderRadius: 999 },
  pillDot: { width: 6, height: 6, borderRadius: 3 },
  pillText: { fontSize: 12, fontWeight: '600' },
  button: { minHeight: 48, borderRadius: 14, paddingHorizontal: 16, flexDirection: 'row', alignItems: 'center', justifyContent: 'center', gap: 8 },
  buttonLarge: { minHeight: 56, borderRadius: 16 },
  button_primary: { backgroundColor: colors.primary, shadowColor: colors.primary, shadowOpacity: 0.28, shadowRadius: 10, shadowOffset: { width: 0, height: 4 }, elevation: 2 },
  button_secondary: { backgroundColor: colors.surface, borderWidth: 1, borderColor: colors.borderStrong },
  button_danger: { backgroundColor: colors.danger },
  button_ghost: { backgroundColor: 'transparent' },
  button_ink: { backgroundColor: colors.ink },
  buttonText: { fontSize: 15, fontWeight: '600', letterSpacing: -0.1 },
  buttonTextLarge: { fontSize: 16 },
  buttonText_primary: { color: colors.white }, buttonText_secondary: { color: colors.text }, buttonText_danger: { color: colors.white },
  buttonText_ghost: { color: colors.textSecondary }, buttonText_ink: { color: colors.white },
  pressed: { opacity: 0.88, transform: [{ scale: 0.985 }] }, disabled: { opacity: 0.45 },
  iconBadge: { alignItems: 'center', justifyContent: 'center' },
  empty: { alignItems: 'center', paddingVertical: 48, paddingHorizontal: 24, gap: 6 },
  emptyIcon: { width: 64, height: 64, borderRadius: 20, backgroundColor: colors.surface, borderWidth: StyleSheet.hairlineWidth, borderColor: colors.borderStrong, alignItems: 'center', justifyContent: 'center', marginBottom: 10 },
  emptyTitle: { fontSize: 17, fontWeight: '700', color: colors.text },
  emptyDescription: { color: colors.textMuted, fontSize: 14, lineHeight: 20, textAlign: 'center', maxWidth: 280 },
  loading: { flex: 1, alignItems: 'center', justifyContent: 'center', backgroundColor: colors.background },
});
