package com.example.jamkkaebi.artifact.domain;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.EnumType;
import jakarta.persistence.Enumerated;
import jakarta.persistence.Id;
import jakarta.persistence.Lob;
import jakarta.persistence.Table;
import lombok.AccessLevel;
import lombok.Builder;
import lombok.Getter;
import lombok.NoArgsConstructor;

/**
 * 유물 마스터 9행.
 *
 * <p>도깨비를 따로 두지 않고 유물 한 행에 합쳤다. 프로필 아바타도 이 {@code id} 로 가리킨다.
 *
 * <p><b>이름 두 개가 한 행에 있다.</b> 무엇을 보여줄지는 사용자의 진행도({@link RevealLevel})가
 * 정한다 — 공개 단계는 사용자별 값이라 저장하지 않는다.
 *
 * <p>id 는 자동 증가가 아니라 고정 번호다. 클라이언트 에셋·도감 카드가 이 번호로
 * 유물을 가리키므로 재시딩해도 번호가 바뀌지 않는다.
 */
@Entity
@Table(name = "artifacts")
@Getter
@NoArgsConstructor(access = AccessLevel.PROTECTED)
public class ArtifactMaster {

    public static final int TOTAL = 9;

    @Id
    private Integer id;

    @Enumerated(EnumType.STRING)
    @Column(nullable = false, length = 30)
    private Era era;

    // 봉인 매개체. 보드 스킨을 정한다.
    @Enumerated(EnumType.STRING)
    @Column(name = "seal_medium", nullable = false, length = 20)
    private SealMedium sealMedium;

    // 시적 별칭
    @Column(name = "poetic_alias", nullable = false, length = 60)
    private String poeticAlias;

    // 실제 이름
    @Column(name = "real_name", nullable = false, length = 60)
    private String realName;

    // 도깨비 기본 이름
    @Column(name = "spirit_default_name", nullable = false, length = 30)
    private String spiritDefaultName;

    // 실제 발굴·복원 기록
    @Lob
    @Column(name = "real_story", nullable = false)
    private String realStory;

    @Column(name = "heritage_url", length = 500)
    private String heritageUrl;

    // 훼손 신고 채널
    @Column(name = "report_url", length = 500)
    private String reportUrl;

    @Column(name = "display_order", nullable = false)
    private int displayOrder;

    @Builder
    private ArtifactMaster(Integer id, Era era, SealMedium sealMedium, String poeticAlias, String realName,
                           String spiritDefaultName, String realStory, String heritageUrl, String reportUrl,
                           int displayOrder) {
        this.id = id;
        this.era = era;
        this.sealMedium = sealMedium;
        this.poeticAlias = poeticAlias;
        this.realName = realName;
        this.spiritDefaultName = spiritDefaultName;
        this.realStory = realStory;
        this.heritageUrl = heritageUrl;
        this.reportUrl = reportUrl;
        this.displayOrder = displayOrder;
    }

    // 시딩이 다시 돌 때 문구만 갱신한다.
    public void refresh(SealMedium sealMedium, String poeticAlias, String realName, String spiritDefaultName,
                        String realStory, String heritageUrl, String reportUrl, int displayOrder) {
        this.sealMedium = sealMedium;
        this.poeticAlias = poeticAlias;
        this.realName = realName;
        this.spiritDefaultName = spiritDefaultName;
        this.realStory = realStory;
        this.heritageUrl = heritageUrl;
        this.reportUrl = reportUrl;
        this.displayOrder = displayOrder;
    }
}
