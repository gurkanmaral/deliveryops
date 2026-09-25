interface PlaceholderPageProps { title: string }

export function PlaceholderPage({ title }: PlaceholderPageProps) {
  return <section><div className="page-heading"><div><p className="eyebrow">YÖNETİM</p><h1>{title}</h1><p>Bu modül bir sonraki dikey dilimde Core API ile birlikte geliştirilecek.</p></div></div><article className="panel empty-state"><span>Yakında</span><h2>{title} modülü için temel rota hazır.</h2><p>Yetkilendirme ve tenant sınırları eklendikten sonra işlemler burada açılacak.</p></article></section>
}
