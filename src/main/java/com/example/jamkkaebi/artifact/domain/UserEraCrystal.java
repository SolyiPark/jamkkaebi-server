package com.example.jamkkaebi.artifact.domain;

import com.example.jamkkaebi.global.common.BaseTimeEntity;
import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.EnumType;
import jakarta.persistence.Enumerated;
import jakarta.persistence.GeneratedValue;
import jakarta.persistence.GenerationType;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import jakarta.persistence.UniqueConstraint;
import lombok.AccessLevel;
import lombok.Getter;
import lombok.NoArgsConstructor;
import org.hibernate.annotations.ColumnDefault;
import org.hibernate.annotations.DynamicUpdate;

/**
 * 시대의 결정 보유량.
 *
 */
@DynamicUpdate
@Entity
@Table(
        name = "user_era_crystals",
        uniqueConstraints = @UniqueConstraint(
                name = "uk_user_era_crystals_owner_era", columnNames = {"user_id", "era"})
)
@Getter
@NoArgsConstructor(access = AccessLevel.PROTECTED)
public class UserEraCrystal extends BaseTimeEntity {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Column(name = "user_id", nullable = false)
    private Long userId;

    @Enumerated(EnumType.STRING)
    @Column(nullable = false, length = 30)
    private Era era;

    @ColumnDefault("0")
    @Column(nullable = false)
    private int crystal;

    private UserEraCrystal(Long userId, Era era) {
        this.userId = userId;
        this.era = era;
    }

    public static UserEraCrystal emptyWallet(Long userId, Era era) {
        return new UserEraCrystal(userId, era);
    }
}
