package com.example.jamkkaebi.artifact.repository;

import com.example.jamkkaebi.artifact.domain.BuildingMaster;
import com.example.jamkkaebi.artifact.domain.Era;
import org.springframework.data.jpa.repository.JpaRepository;

import java.util.Optional;

public interface BuildingMasterRepository extends JpaRepository<BuildingMaster, Integer> {

    Optional<BuildingMaster> findByEra(Era era);
}
