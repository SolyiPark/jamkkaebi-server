package com.example.jamkkaebi.artifact.domain;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.EnumType;
import jakarta.persistence.Enumerated;
import jakarta.persistence.Id;
import jakarta.persistence.Lob;
import jakarta.persistence.Table;
import jakarta.persistence.UniqueConstraint;
import lombok.AccessLevel;
import lombok.Builder;
import lombok.Getter;
import lombok.NoArgsConstructor;

/**
 * 건물 마스터 3행.
 *
 * <p><b>건물은 뽑기·플레이 대상이 아니다.</b> 그 시대의 유물 3종을 모두 완료하면 도감 완성 보상으로
 * 자동 지급된다.
 */
@Entity
@Table(name = "buildings",
        uniqueConstraints = @UniqueConstraint(name = "uk_buildings_era", columnNames = "era"))
@Getter
@NoArgsConstructor(access = AccessLevel.PROTECTED)
public class BuildingMaster {

    // 전 건물 공용 최대 레벨 (Lv.1 기본 완공 → Lv.3 완전판)
    public static final int MAX_LEVEL = 3;

    @Id
    private Integer id;

    @Enumerated(EnumType.STRING)
    @Column(nullable = false, length = 30)
    private Era era;

    @Column(nullable = false, length = 60)
    private String name;

    // 완공 연출의 자막
    @Lob
    @Column(name = "real_story", nullable = false)
    private String realStory;

    @Builder
    private BuildingMaster(Integer id, Era era, String name, String realStory) {
        this.id = id;
        this.era = era;
        this.name = name;
        this.realStory = realStory;
    }

    public void refresh(String name, String realStory) {
        this.name = name;
        this.realStory = realStory;
    }
}
